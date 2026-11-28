#!/usr/bin/env python3
"""
Carry the second diet reader's readings from one copy of the recipes to another.

The second reader (lane diet-second-reader: mistral-small and phi4) costs about 35 s of model time
a recipe. A reading made once, on staging's copy of the same recipes or by the offline scorer
(Nom.Import --golden-score --lane diet-second-reader --answers), is taken again instead of reading
the recipe twice, where it matches exactly: a model of the pair, the reader's current prompt
signature, and the signature of the recipe text it was made of equal to the recipe's text now.
nom-api checks each of these and leaves every other reading, counted by its reason (design:
docs/architecture/diet-second-reader.md, "Readings that travel").

RULES
-----
1. export writes the stored readings of one nom-api, page by page, as JSON lines (recipe id,
   model, prompt and text signatures, the answer per type). Readings hold verdict words and food
   words only, never a nutrition number.
2. import reads JSON lines of an export, or the offline scorer's answers files. A scorer's line
   written since plan item ck23 carries its model, prompt and text signature; an older one carries
   none, so --model and --prompt (from the run's report, e.g. 27de91bd21cc1e9f) must be given and
   --set names the scorer's set (its diet-second-reader.tsv, or a folder holding such files): the
   recipe's title, lines and method there are sent as the text, and nom-api signs them as the lane
   does. A line whose text cannot be found is not sent.
3. Nothing is fetched but nom-api, authenticated as a curation admin (X-Api-Key or Bearer). A dry
   run unless --apply; every reading taken is journaled (DietReadingImport) under the stamp nom-api
   returns, one stamp per batch of 5,000. undo STAMP takes a batch back (a dry run unless --apply);
   a reading changed since is skipped.

USAGE
-----
    export NOM_API=https://nom.example.com NOM_API_KEY=...
    ./ops/diet-readings.py export --out readings.jsonl
    ./ops/diet-readings.py import readings.jsonl [--apply]
    ./ops/diet-readings.py import ans/mistral-small_24b.c00.jsonl --model mistral-small:24b --prompt 27de91bd21cc1e9f --set sets/ [--apply]
    ./ops/diet-readings.py undo 2026-11-28T10:00:00.000Z [--apply]
"""

from __future__ import annotations

import argparse
import csv
import json
import os
import sys
import urllib.error
import urllib.parse
import urllib.request

EXPORT_PATH = "/api/DietAdmin/second-reader/readings"
IMPORT_PATH = "/api/DietAdmin/second-reader/readings/import"
UNDO_PATH = "/api/DietAdmin/second-reader/readings/import/undo"
USER_AGENT = "nom-ops-diet-readings/1"
SET_FILE = "diet-second-reader.tsv"
STEP_SEPARATOR = " ¦ "
BATCH = 5000


class NomApi:
    def __init__(self, base: str, api_key: str | None, token: str | None, timeout: int = 600):
        self.base = base.rstrip("/")
        self.headers = {"Content-Type": "application/json", "Accept": "application/json", "User-Agent": USER_AGENT}
        if api_key:
            self.headers["X-Api-Key"] = api_key
        elif token:
            self.headers["Authorization"] = f"Bearer {token}"
        self.timeout = timeout

    def call(self, method: str, path: str, params: dict, body=None):
        url = self.base + path + "?" + urllib.parse.urlencode(params)
        data = json.dumps(body).encode() if body is not None else (b"" if method == "POST" else None)
        req = urllib.request.Request(url, method=method, headers=self.headers, data=data)
        with urllib.request.urlopen(req, timeout=self.timeout) as resp:
            raw = resp.read().decode()
            return json.loads(raw) if raw else None


def split_cell(cell: str) -> list[str]:
    return [] if cell in ("", "-") else cell.split(STEP_SEPARATOR)


def read_sets(paths: list[str]) -> dict[int, dict]:
    """Each recipe's text in the scorer's sets: {recipe id: {title, lines, method}}."""
    files: list[str] = []
    for path in paths:
        if os.path.isdir(path):
            for folder, _, names in sorted(os.walk(path)):
                files.extend(os.path.join(folder, n) for n in sorted(names) if n == SET_FILE)
        else:
            files.append(path)
    texts: dict[int, dict] = {}
    for path in files:
        with open(path, encoding="utf-8", newline="") as handle:
            reader = csv.DictReader((line for line in handle if not line.startswith("#")), delimiter="\t", quoting=csv.QUOTE_NONE)
            for row in reader:
                if not (row.get("recipe") or "").isdigit():
                    continue
                texts.setdefault(int(row["recipe"]), {
                    "title": row.get("title") or "",
                    "lines": split_cell(row.get("lines") or ""),
                    "method": split_cell(row.get("method") or ""),
                })
    return texts


def items_of(path: str, model: str | None, prompt: str | None, texts: dict[int, dict]) -> tuple[list[dict], list[str]]:
    """The import items of a JSON-lines file (an export or a scorer's answers), and the lines left out."""
    items: list[dict] = []
    problems: list[str] = []
    with open(path, encoding="utf-8") as handle:
        for number, line in enumerate(handle, start=1):
            if not line.strip():
                continue
            label = f"{path}:{number}"
            entry = json.loads(line)
            if "recipeId" in entry:
                items.append({k: entry.get(k) for k in ("recipeId", "model", "promptSignature", "textSignature", "readable", "complete", "unlisted", "answers", "seconds", "readAt")})
                continue
            recipe = entry.get("recipe")
            if not isinstance(recipe, int):
                problems.append(f"{label}: no recipe id")
                continue
            item = {
                "recipeId": recipe,
                "model": entry.get("model") or model,
                "promptSignature": entry.get("prompt") or prompt,
                "readable": entry.get("answers") is not None,
                "complete": bool(entry.get("complete")),
                "unlisted": entry.get("unlisted") or [],
                "answers": entry.get("answers"),
            }
            if not item["model"] or not item["promptSignature"]:
                problems.append(f"{label}: no model or prompt signature (give --model and --prompt for an older answers file)")
                continue
            if entry.get("text"):
                item["textSignature"] = entry["text"]
            elif recipe in texts:
                item["text"] = texts[recipe]
            else:
                problems.append(f"{label}: recipe {recipe} has no text signature and is in no --set")
                continue
            items.append(item)
    return items, problems


def merge(reports: list[dict]) -> dict:
    total = {"applied": any(r.get("applied") for r in reports), "stamps": [r.get("stamp") for r in reports], "received": 0, "taken": 0, "takenByModel": {}, "skipped": {}}
    for r in reports:
        total["received"] += r.get("received", 0)
        total["taken"] += r.get("taken", 0)
        for key in ("takenByModel", "skipped"):
            for k, v in (r.get(key) or {}).items():
                total[key][k] = total[key].get(k, 0) + v
    return total


def main(argv: list[str] | None = None, call=None) -> int:
    parser = argparse.ArgumentParser(description="Export and import the second diet reader's readings (exact signatures only).")
    sub = parser.add_subparsers(dest="command", required=True)
    ex = sub.add_parser("export", help="write the stored readings as JSON lines")
    ex.add_argument("--out", required=True)
    ex.add_argument("--all", action="store_true", help="also readings that are unreadable or of another prompt")
    im = sub.add_parser("import", help="send readings; a dry run unless --apply")
    im.add_argument("files", nargs="+")
    im.add_argument("--model")
    im.add_argument("--prompt")
    im.add_argument("--set", action="append", default=[], help="the scorer's diet-second-reader.tsv, or a folder of them")
    im.add_argument("--apply", action="store_true")
    un = sub.add_parser("undo", help="take back an import batch by its stamp; a dry run unless --apply")
    un.add_argument("stamp")
    un.add_argument("--apply", action="store_true")
    parser.add_argument("--api", default=os.environ.get("NOM_API", ""))
    args = parser.parse_args(argv)
    if call is None and args.api:
        call = NomApi(args.api, os.environ.get("NOM_API_KEY"), os.environ.get("NOM_API_TOKEN")).call

    if args.command == "import":
        texts = read_sets(args.set)
        items: list[dict] = []
        problems: list[str] = []
        for path in args.files:
            file_items, file_problems = items_of(path, args.model, args.prompt, texts)
            items.extend(file_items)
            problems.extend(file_problems)
        for problem in problems:
            print(problem, file=sys.stderr)
        print(json.dumps({"send": len(items), "notSent": len(problems)}), file=sys.stderr)
        if call is None or not items:
            return 0 if not problems else 2
        try:
            reports = [call("POST", IMPORT_PATH, {"apply": str(args.apply).lower()}, items[i:i + BATCH]) for i in range(0, len(items), BATCH)]
        except urllib.error.HTTPError as exc:
            print(f"nom-api answered HTTP {exc.code}", file=sys.stderr)
            return 2
        print(json.dumps(merge(reports), indent=2))
        if args.apply:
            for r in reports:
                if r and r.get("taken"):
                    print(f"undo with: undo {r['stamp']}", file=sys.stderr)
        return 0 if not problems else 2

    if call is None:
        parser.error("set NOM_API or --api")
    if args.command == "undo":
        print(json.dumps(call("POST", UNDO_PATH, {"stamp": args.stamp, "apply": str(args.apply).lower()}), indent=2))
        return 0

    after = 0
    written = 0
    with open(args.out, "w", encoding="utf-8") as out:
        while True:
            page = call("GET", EXPORT_PATH, {"after": after, "recipes": 1000, "currentOnly": str(not args.all).lower()})
            for item in page.get("items") or []:
                out.write(json.dumps(item, ensure_ascii=False) + "\n")
                written += 1
            if page.get("next") is None:
                break
            after = page["next"]
    print(json.dumps({"readings": written, "out": args.out}), file=sys.stderr)
    return 0


if __name__ == "__main__":
    sys.exit(main())

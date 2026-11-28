#!/usr/bin/env python3
"""
Load the judged misses of the diet draws and staging checkpoints into nom-api as judged holds.

A recipe people judged to hide a food a restriction type keeps out, while the diet gate's rules
showed it, is held by the stored gate for that type as "Judged to contain" once it is loaded
(plan.RecipeDietJudgedHold; design: docs/architecture/diet-gate.md, "Judged holds"). A miss once
found then stays held whatever the rules do. A judged hold never clears a recipe.

RULES
-----
1. The input is a TSV with the columns recipe, type, food, judged_by, judged_at, evidence: the
   recipe id, the restriction type's name (e.g. "Wheat Allergy"), a short word for the food (never
   recipe text), who judged it, the date, and the name of the evidence file that holds the
   judgement. The judged files themselves hold web recipe text and stay outside the public repo;
   ops/diet-judged-holds/judged-misses.tsv carries ids, types, food words and file names only.
2. A row with a missing column, a recipe id that is not a number, a date that is not YYYY-MM-DD or
   a food word longer than 60 characters is not sent. A (recipe, type) given twice is sent once.
3. Nothing is fetched. The one network peer is nom-api, authenticated as a curation admin
   (X-Api-Key or Bearer). Without NOM_API the script only prints what it would send.
4. A dry run unless --apply; nom-api refuses a type that is no restriction type and a recipe that is
   not live, leaves a (recipe, type) already held as it is, and journals every hold it adds
   (DietJudgedHoldLoad) under the stamp it returns. A Wheat Allergy hold is also held for the gluten
   types (a wheat food holds gluten); a gluten food made of wheat is given for Wheat Allergy here. --undo STAMP takes that load back (a dry run
   unless --apply).

USAGE
-----
    ./ops/diet-judged-holds.py ops/diet-judged-holds/judged-misses.tsv                     # print only
    export NOM_API=https://nom.example.com NOM_API_KEY=...
    ./ops/diet-judged-holds.py ops/diet-judged-holds/judged-misses.tsv                     # dry run in nom-api
    ./ops/diet-judged-holds.py ops/diet-judged-holds/judged-misses.tsv --apply
    ./ops/diet-judged-holds.py --undo 2026-11-28T10:00:00.000Z [--apply]
"""

from __future__ import annotations

import argparse
import csv
import datetime
import json
import os
import re
import sys
import urllib.error
import urllib.parse
import urllib.request

LOAD_PATH = "/api/DietAdmin/judged-holds"
UNDO_PATH = "/api/DietAdmin/judged-holds/undo"
USER_AGENT = "nom-ops-diet-judged-holds/1"
COLUMNS = ("recipe", "type", "food", "judged_by", "judged_at", "evidence")
MAX_FOOD = 60
BATCH = 5000
DATE = re.compile(r"^\d{4}-\d{2}-\d{2}$")


def read_rows(path: str) -> tuple[list[dict], list[str]]:
    """The rows to send and the reasons rows were left out, from a judged-holds TSV."""
    rows: list[dict] = []
    problems: list[str] = []
    seen: set[tuple[int, str]] = set()
    with open(path, encoding="utf-8", newline="") as handle:
        reader = csv.DictReader((line for line in handle if not line.startswith("#")), delimiter="\t", quoting=csv.QUOTE_NONE)
        missing = [c for c in COLUMNS if c not in (reader.fieldnames or [])]
        if missing:
            raise ValueError(f"{path}: missing columns {', '.join(missing)}")
        for number, raw in enumerate(reader, start=2):
            row = {c: (raw.get(c) or "").strip() for c in COLUMNS}
            label = f"{path}:{number}"
            if any(not row[c] for c in COLUMNS):
                problems.append(f"{label}: every column is needed")
                continue
            if not row["recipe"].isdigit():
                problems.append(f"{label}: recipe {row['recipe']!r} is not an id")
                continue
            if not DATE.match(row["judged_at"]):
                problems.append(f"{label}: judged_at {row['judged_at']!r} is not YYYY-MM-DD")
                continue
            if len(row["food"]) > MAX_FOOD:
                problems.append(f"{label}: the food is not a short word")
                continue
            key = (int(row["recipe"]), row["type"].lower())
            if key in seen:
                problems.append(f"{label}: recipe {row['recipe']} and {row['type']} given twice")
                continue
            seen.add(key)
            rows.append({
                "recipeId": int(row["recipe"]),
                "type": row["type"],
                "food": row["food"],
                "judgedBy": row["judged_by"],
                "judgedAt": datetime.date.fromisoformat(row["judged_at"]).isoformat() + "T00:00:00Z",
                "evidence": row["evidence"],
            })
    return rows, problems


class NomApi:
    def __init__(self, base: str, api_key: str | None, token: str | None, timeout: int = 300):
        self.base = base.rstrip("/")
        self.headers = {"Content-Type": "application/json", "Accept": "application/json", "User-Agent": USER_AGENT}
        if api_key:
            self.headers["X-Api-Key"] = api_key
        elif token:
            self.headers["Authorization"] = f"Bearer {token}"
        self.timeout = timeout

    def post(self, path: str, params: dict, body=None):
        url = self.base + path + "?" + urllib.parse.urlencode(params)
        req = urllib.request.Request(url, method="POST", headers=self.headers, data=json.dumps(body).encode() if body is not None else b"")
        with urllib.request.urlopen(req, timeout=self.timeout) as resp:
            raw = resp.read().decode()
            return json.loads(raw) if raw else None


def main(argv: list[str] | None = None, post=None) -> int:
    parser = argparse.ArgumentParser(description="Load judged diet misses into nom-api as judged holds (no fetching).")
    parser.add_argument("files", nargs="*", help="judged-holds TSV files")
    parser.add_argument("--apply", action="store_true", help="store the holds; without it nom-api writes nothing")
    parser.add_argument("--undo", help="take back the load with this stamp instead")
    parser.add_argument("--api", default=os.environ.get("NOM_API", ""))
    args = parser.parse_args(argv)
    if post is None and args.api:
        post = NomApi(args.api, os.environ.get("NOM_API_KEY"), os.environ.get("NOM_API_TOKEN")).post

    if args.undo:
        if args.files:
            parser.error("--undo takes no files")
        if post is None:
            parser.error("set NOM_API or --api to undo")
        print(json.dumps(post(UNDO_PATH, {"stamp": args.undo, "apply": str(args.apply).lower()}), indent=2))
        return 0

    if not args.files:
        parser.error("no files given")
    rows: list[dict] = []
    problems: list[str] = []
    keys: set[tuple[int, str]] = set()
    for path in args.files:
        file_rows, file_problems = read_rows(path)
        problems.extend(file_problems)
        for row in file_rows:
            key = (row["recipeId"], row["type"].lower())
            if key in keys:
                problems.append(f"{path}: recipe {row['recipeId']} and {row['type']} given in an earlier file")
                continue
            keys.add(key)
            rows.append(row)
    for problem in problems:
        print(problem, file=sys.stderr)
    by_type: dict[str, int] = {}
    for row in rows:
        by_type[row["type"]] = by_type.get(row["type"], 0) + 1
    print(json.dumps({"send": len(rows), "notSent": len(problems), "byType": dict(sorted(by_type.items()))}), file=sys.stderr)
    if post is None or not rows:
        for row in rows:
            print("\t".join([str(row["recipeId"]), row["type"], row["food"], row["judgedBy"], row["judgedAt"][:10], row["evidence"]]))
        return 0 if not problems else 2
    answers = []
    try:
        for start in range(0, len(rows), BATCH):
            answers.append(post(LOAD_PATH, {"apply": str(args.apply).lower()}, rows[start:start + BATCH]))
    except urllib.error.HTTPError as exc:
        print(f"nom-api answered HTTP {exc.code}", file=sys.stderr)
        return 2
    print(json.dumps(answers if len(answers) > 1 else answers[0], indent=2))
    if args.apply:
        for answer in answers:
            if answer and answer.get("stamp"):
                print(f"undo with: --undo {answer['stamp']}", file=sys.stderr)
    return 0 if not problems and not any((a or {}).get("refused") for a in answers) else 2


if __name__ == "__main__":
    sys.exit(main())

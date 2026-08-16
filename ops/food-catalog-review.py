#!/usr/bin/env python3
"""
Batch review of the NOM food catalog by a local Claude Code instance.

    export nom-catalog CSV  ->  batched review  ->  proposals CSV  ->  admin approves

WHAT THE REVIEWER IS ALLOWED TO DO
----------------------------------
Categorical judgement only: classify food groups, flag suspicious rows, normalize
names, and name foods that look missing. It must NOT author nutrition numbers.
A language model has no authoritative nutrition data, so a model-supplied value
would replace a measured number with a plausible guess — worse than no change.
The API enforces this too (ProposalPolicy): a nutrient-field proposal is rejected
unless its source is authoritative (fdc:/label:/admin:/deterministic:). This
script never emits one; the server check is the backstop, not the only guard.

To actually verify numbers, re-run the FDC import against the latest release and
diff by FdcId — deterministic, authoritative, free. See
docs/architecture/food-catalog-ingestion.md.

USAGE
-----
    # 1. export the catalog (admin cookie/token required)
    curl -s -H "Authorization: Bearer $NOM_TOKEN" \
        "$NOM_API/api/FoodCatalog/export?source=branded_food&limit=2000" > catalog.csv

    # 2. review it (writes proposals.csv)
    ./ops/food-catalog-review.py catalog.csv --out proposals.csv --batch-size 40

    # 3. ingest the proposals for admin review in the app
    jq -Rs '{csv: ., batch: "review-2026-08-15"}' proposals.csv | \
      curl -s -X POST -H "Authorization: Bearer $NOM_TOKEN" \
           -H 'Content-Type: application/json' --data @- \
           "$NOM_API/api/FoodCatalog/proposals"

Requires the `claude` CLI on PATH (headless `-p` mode).
"""

from __future__ import annotations

import argparse
import csv
import io
import json
import subprocess
import sys
from datetime import date

# Fields the reviewer may propose. Anything nutritional is deliberately absent.
ALLOWED_FIELDS = {"name", "food_group", "is_whole_food"}

FOOD_GROUPS = [
    "Vegetables", "Fruits", "Grains", "Protein Foods", "Dairy",
    "Fats/Oils", "Legumes", "Nuts/Seeds", "Sweets/Snacks", "Beverages",
]

PROMPT = f"""You are auditing rows from a food catalog. Reply with ONLY a JSON array, no prose.

For each input row, decide whether anything looks wrong. Output one object per row you
want to change or flag — omit rows that look fine.

Object shape:
  {{"ingredient_id": <int>,
    "action": "update" | "flag",
    "field": "name" | "food_group" | "is_whole_food" | null,
    "proposed_value": <string|null>,
    "confidence": <0..1>,
    "reason": "<short>"}}

Rules you MUST follow:
- NEVER propose a nutrition number (calories, protein, carbs, fat, serving grams).
  You have no authoritative nutrition data. If a value looks wrong, use
  action "flag" with a reason and leave field/proposed_value null.
- food_group must be exactly one of: {", ".join(FOOD_GROUPS)}.
- Condiments, sauces, dressings and seasonings belong to NO food group. Do not
  assign one; flag instead if the current group is wrong.
- "is_whole_food" means a person would eat it as-is (an apple, a protein bar),
  as opposed to a cooking ingredient (flour, raw chicken, oil).
- For name changes, only normalize retail shouting into readable text
  ("CHOCOLATE DRINK MIX, CHOCOLATE" -> "Chocolate Drink Mix"). Do not invent brands.
- If nothing is wrong with a row, output nothing for it.

Rows (CSV):
"""

MISSING_PROMPT = """You are checking a food catalog for coverage gaps. Reply with ONLY a JSON array.

Below are the food names currently in the catalog for the category "{category}".
List common foods an ordinary household would expect that are MISSING from this list.

Object shape: {{"name": "<food name>", "reason": "<why it's commonly needed>"}}

Rules:
- Names only. Do NOT provide nutrition values; the importer resolves each name
  against USDA FoodData Central to get real measured numbers.
- Everyday foods only — no rarities.
- At most 25 entries.

Current catalog for {category}:
"""


def run_claude(prompt: str, model: str | None, timeout: int) -> str:
    """Invoke the Claude CLI headlessly and return its stdout."""
    cmd = ["claude", "-p", prompt]
    if model:
        cmd += ["--model", model]
    try:
        done = subprocess.run(cmd, capture_output=True, text=True, timeout=timeout, check=False)
    except FileNotFoundError:
        sys.exit("error: `claude` CLI not found on PATH.")
    except subprocess.TimeoutExpired:
        print("  ! batch timed out; skipping", file=sys.stderr)
        return ""
    if done.returncode != 0:
        print(f"  ! claude exited {done.returncode}: {done.stderr.strip()[:200]}", file=sys.stderr)
        return ""
    return done.stdout


def parse_json_array(text: str) -> list[dict]:
    """Pull the first JSON array out of a reply, tolerating surrounding prose."""
    start, end = text.find("["), text.rfind("]")
    if start < 0 or end <= start:
        return []
    try:
        parsed = json.loads(text[start : end + 1])
        return [x for x in parsed if isinstance(x, dict)]
    except json.JSONDecodeError:
        return []


def review_batches(rows: list[dict], batch_size: int, model: str | None,
                   timeout: int) -> list[dict]:
    """Send the catalog through the reviewer in batches; collect raw suggestions."""
    suggestions: list[dict] = []
    for start in range(0, len(rows), batch_size):
        chunk = rows[start : start + batch_size]
        buf = io.StringIO()
        writer = csv.DictWriter(
            buf,
            fieldnames=["ingredient_id", "name", "food_group", "is_whole_food",
                        "kcal_per_100g", "protein_per_100g", "carb_per_100g", "fat_per_100g"],
            extrasaction="ignore",
        )
        writer.writeheader()
        writer.writerows(chunk)

        n = start // batch_size + 1
        total = (len(rows) + batch_size - 1) // batch_size
        print(f"  batch {n}/{total} ({len(chunk)} rows)…", file=sys.stderr)
        suggestions += parse_json_array(run_claude(PROMPT + buf.getvalue(), model, timeout))
    return suggestions


def to_proposals(suggestions: list[dict], by_id: dict[str, dict], source: str) -> list[dict]:
    """Validate suggestions and shape them into proposal rows. Drops anything unsafe."""
    out, dropped = [], 0
    for s in suggestions:
        action = str(s.get("action", "")).lower()
        if action not in {"update", "flag"}:
            dropped += 1
            continue

        field = s.get("field") or None
        if action == "update":
            if field not in ALLOWED_FIELDS:
                dropped += 1  # nutrition or unknown field — never allowed from a reviewer
                continue
            if field == "food_group" and s.get("proposed_value") not in FOOD_GROUPS:
                dropped += 1  # hallucinated group
                continue
        else:
            field = None  # flags change nothing

        ing_id = str(s.get("ingredient_id", "")).strip()
        current = by_id.get(ing_id, {})
        if not ing_id or not current:
            dropped += 1
            continue

        current_value = ""
        if field == "name":
            current_value = current.get("name", "")
        elif field == "food_group":
            current_value = current.get("food_group", "")
        elif field == "is_whole_food":
            current_value = current.get("is_whole_food", "")

        out.append({
            "action": action,
            "ingredient_id": ing_id,
            "fdc_id": current.get("fdc_id", ""),
            "field": field or "",
            "current_value": current_value,
            "proposed_value": s.get("proposed_value") or "",
            "confidence": s.get("confidence", ""),
            "reason": (s.get("reason") or "")[:500],
            "source": source,
        })

    if dropped:
        print(f"  dropped {dropped} unsafe/invalid suggestion(s)", file=sys.stderr)
    return out


def find_gaps(rows: list[dict], model: str | None, timeout: int, source: str) -> list[dict]:
    """Ask which everyday foods are missing, per food group. Names only — no numbers."""
    proposals = []
    by_group: dict[str, list[str]] = {}
    for r in rows:
        by_group.setdefault(r.get("food_group") or "Unclassified", []).append(r.get("name", ""))

    for group, names in by_group.items():
        if group == "Unclassified" or len(names) < 3:
            continue
        print(f"  gaps in {group}…", file=sys.stderr)
        prompt = MISSING_PROMPT.format(category=group) + "\n".join(sorted(names)[:400])
        for item in parse_json_array(run_claude(prompt, model, timeout)):
            name = (item.get("name") or "").strip()
            if not name:
                continue
            proposals.append({
                "action": "add", "ingredient_id": "", "fdc_id": "", "field": "name",
                "current_value": "", "proposed_value": name, "confidence": "",
                "reason": f"[{group}] {(item.get('reason') or 'commonly expected')[:300]}",
                "source": source,
            })
    return proposals


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("catalog_csv", help="CSV from GET /api/FoodCatalog/export")
    ap.add_argument("--out", default="proposals.csv")
    ap.add_argument("--batch-size", type=int, default=40, help="rows per review call")
    ap.add_argument("--limit", type=int, default=0, help="only review the first N rows (0 = all)")
    ap.add_argument("--model", default=None, help="override the CLI model")
    ap.add_argument("--timeout", type=int, default=300, help="seconds per batch")
    ap.add_argument("--gaps", action="store_true", help="also ask what common foods are missing")
    args = ap.parse_args()

    with open(args.catalog_csv, newline="", encoding="utf-8") as fh:
        rows = list(csv.DictReader(fh))
    if args.limit:
        rows = rows[: args.limit]
    if not rows:
        sys.exit("no rows to review")

    source = f"review:claude/{date.today().isoformat()}"
    by_id = {r.get("ingredient_id", ""): r for r in rows}

    print(f"Reviewing {len(rows)} rows…", file=sys.stderr)
    proposals = to_proposals(review_batches(rows, args.batch_size, args.model, args.timeout),
                             by_id, source)
    if args.gaps:
        print("Checking coverage gaps…", file=sys.stderr)
        proposals += find_gaps(rows, args.model, args.timeout, source)

    fields = ["action", "ingredient_id", "fdc_id", "field", "current_value",
              "proposed_value", "confidence", "reason", "source"]
    with open(args.out, "w", newline="", encoding="utf-8") as fh:
        writer = csv.DictWriter(fh, fieldnames=fields)
        writer.writeheader()
        writer.writerows(proposals)

    print(f"Wrote {len(proposals)} proposal(s) to {args.out}", file=sys.stderr)
    print("Nothing is applied until an admin approves it in Admin -> Food Catalog.", file=sys.stderr)


if __name__ == "__main__":
    main()

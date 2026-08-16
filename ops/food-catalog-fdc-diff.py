#!/usr/bin/env python3
"""
Freshness diff: catalog vs the CURRENT USDA FoodData Central release.

This is the honest answer to "does our data still match today's values". USDA is the
origin of every FDC-sourced row we hold and is CC0, so if the current release differs
from our copy, our copy is stale — no model, no web fetch, no guessing involved.

    catalog CSV + current FDC CSVs  ->  compare by FdcId  ->  proposals CSV

Differences are proposed as numeric `update`s with an `fdc:<id>` source, which
ProposalPolicy accepts as authoritative — and which an admin still has to approve.
Rows whose FdcId has vanished from the release are proposed as `flag` (withdrawn
upstream), never auto-deleted.

USAGE
-----
    # current release (no account needed for bulk downloads)
    curl -sLO https://fdc.nal.usda.gov/fdc-datasets/FoodData_Central_foundation_food_csv_2025-12-18.zip
    unzip -q FoodData_Central_foundation_food_csv_2025-12-18.zip

    curl -s -H "Authorization: Bearer $NOM_TOKEN" \
        "$NOM_API/api/FoodCatalog/export?source=foundation_food&limit=20000" > catalog.csv

    ./ops/food-catalog-fdc-diff.py catalog.csv FoodData_Central_foundation_food_csv_2025-12-18/ \
        --out fdc-diff-proposals.csv
"""

from __future__ import annotations

import argparse
import os
import sys
from datetime import date

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from catalog_compare import (  # noqa: E402
    CompareStats, diff_row, load_catalog, print_stats, proposal, write_proposals,
)

# Energy may be reported as general Energy or under either Atwater factor.
NUT_ENERGY, NUT_ATWATER_SPECIFIC, NUT_ATWATER_GENERAL = "1008", "2048", "2047"
NUT_PROTEIN, NUT_CARB, NUT_FAT = "1003", "1005", "1004"


def split_csv(line: str) -> list[str]:
    fields, buf, in_quotes = [], [], False
    i = 0
    while i < len(line):
        ch = line[i]
        if in_quotes:
            if ch == '"':
                if i + 1 < len(line) and line[i + 1] == '"':
                    buf.append('"')
                    i += 1
                else:
                    in_quotes = False
            else:
                buf.append(ch)
        elif ch == '"':
            in_quotes = True
        elif ch == ",":
            fields.append("".join(buf))
            buf = []
        else:
            buf.append(ch)
        i += 1
    fields.append("".join(buf))
    return fields


def find_file(directory: str, name: str) -> str:
    direct = os.path.join(directory, name)
    if os.path.exists(direct):
        return direct
    for root, _dirs, files in os.walk(directory):
        if name in files:
            return os.path.join(root, name)
    sys.exit(f"error: {name} not found under {directory}")


def read_current_nutrients(path: str, wanted: set[str]) -> dict[str, dict[str, float | None]]:
    """Per-100 g macros for the wanted FdcIds, straight from the release (streamed)."""
    acc: dict[str, dict[str, float]] = {}
    with open(path, newline="", encoding="utf-8", errors="replace") as fh:
        fh.readline()
        for line in fh:
            f = split_csv(line.rstrip("\n\r"))
            if len(f) < 4 or f[1] not in wanted:
                continue
            try:
                amount = float(f[3])
            except ValueError:
                continue
            acc.setdefault(f[1], {})[f[2]] = amount

    out: dict[str, dict[str, float | None]] = {}
    for fdc_id, values in acc.items():
        energy = (values.get(NUT_ENERGY)
                  or values.get(NUT_ATWATER_SPECIFIC)
                  or values.get(NUT_ATWATER_GENERAL))
        out[fdc_id] = {
            "kcal_per_100g": energy,
            "protein_per_100g": values.get(NUT_PROTEIN),
            "carb_per_100g": values.get(NUT_CARB),
            "fat_per_100g": values.get(NUT_FAT),
        }
    return out


def read_present_ids(path: str) -> set[str]:
    present = set()
    with open(path, newline="", encoding="utf-8", errors="replace") as fh:
        fh.readline()
        for line in fh:
            f = split_csv(line.rstrip("\n\r"))
            if f:
                present.add(f[0])
    return present


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("catalog_csv", help="CSV from GET /api/FoodCatalog/export")
    ap.add_argument("fdc_dir", help="unzipped current FDC CSV directory")
    ap.add_argument("--out", default="fdc-diff-proposals.csv")
    ap.add_argument("--limit", type=int, default=0)
    args = ap.parse_args()

    rows = [r for r in load_catalog(args.catalog_csv, args.limit) if r.get("fdc_id")]
    if not rows:
        sys.exit("no FDC-sourced rows in the catalog export")

    wanted = {r["fdc_id"] for r in rows}
    print(f"Comparing {len(rows)} FDC-sourced rows against the current release…",
          file=sys.stderr)

    present = read_present_ids(find_file(args.fdc_dir, "food.csv"))
    current = read_current_nutrients(find_file(args.fdc_dir, "food_nutrient.csv"), wanted)

    stats, proposals = CompareStats(rows=len(rows)), []
    today = date.today().isoformat()

    for row in rows:
        fdc_id = row["fdc_id"]

        if fdc_id not in present:
            stats.unmatched += 1
            proposals.append(proposal(
                action="flag", ingredient_id=row.get("ingredient_id", ""), fdc_id=fdc_id,
                field_name="", current_value=None, proposed_value=None, confidence="0.60",
                reason=("No longer present in the current USDA release — the record may have "
                        "been withdrawn or renumbered. Needs a human before removal."),
                source=f"fdc:{fdc_id}"))
            continue

        theirs = current.get(fdc_id)
        if not theirs:
            stats.unmatched += 1
            continue

        stats.matched += 1
        differences = diff_row(row, theirs)
        if not differences:
            stats.agreed += 1
            continue

        stats.differing += 1
        for d in differences:
            stats.by_column[d.column] = stats.by_column.get(d.column, 0) + 1
            if d.ours is None:
                stats.missing_locally += 1
            proposals.append(proposal(
                action="update", ingredient_id=row.get("ingredient_id", ""), fdc_id=fdc_id,
                field_name=d.column, current_value=d.ours, proposed_value=round(d.theirs, 2),
                confidence="0.95",
                reason=(f"Current USDA release reports {d.theirs:.2f} per 100 g for {d.label}; "
                        f"our copy has {'nothing' if d.ours is None else f'{d.ours:g}'}. "
                        f"USDA is the origin of this record, so our copy is stale."),
                source=f"fdc:{fdc_id}"))

    write_proposals(args.out, proposals)
    print_stats("FDC freshness diff", stats, proposals)
    print("Nothing is applied until an admin approves it in Admin -> Food Catalog.",
          file=sys.stderr)


if __name__ == "__main__":
    main()

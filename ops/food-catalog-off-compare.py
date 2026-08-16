#!/usr/bin/env python3
"""
Cross-check the catalog against the Open Food Facts bulk export — as a SIGNAL ONLY.

OFF is the only source with broad UPC coverage, which is what our branded catalog
needs (36,743 distinct brand owners, mostly retailer private label). But it is
crowd-sourced and licensed **ODbL**, so this tool is deliberately built to take a
*signal*, never data:

  * Every output is a `flag` for a human. Nothing is ever an `update`.
  * OFF's numbers are NEVER written into `proposed_value`, and never enter the
    catalog. The proposal records that a disagreement exists and how large it is,
    expressed as a percentage difference, so no OFF value is copied.
  * That keeps the ODbL share-alike surface at zero: we are not incorporating their
    database into ours, only consulting it. (Confirm with counsel before shipping.)

We use the **nightly bulk export**, never the API or a crawler. OFF's own guidance is
"1 API call = 1 real scan by a user", and their robots.txt disallows /api — batch
checking is not what the API is for, and the export puts zero load on them.

    catalog CSV + OFF export  ->  match by barcode  ->  flags CSV

USAGE
-----
    curl -sLO https://static.openfoodfacts.org/data/en.openfoodfacts.org.products.csv.gz

    curl -s -H "Authorization: Bearer $NOM_TOKEN" \
        "$NOM_API/api/FoodCatalog/export?source=branded_food&limit=20000" > catalog.csv

    ./ops/food-catalog-off-compare.py catalog.csv en.openfoodfacts.org.products.csv.gz \
        --out off-flags.csv
"""

from __future__ import annotations

import argparse
import csv
import gzip
import io
import os
import sys
from datetime import date

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from catalog_compare import (  # noqa: E402
    COMPARED_COLUMNS, CompareStats, diff_row, load_catalog, print_stats, proposal,
    to_float, write_proposals,
)

# OFF publishes per-100g columns directly, so no mass conversion is needed for these.
# (Their serving-size fields are free text and are deliberately not relied on here.)
OFF_COLUMNS = {
    "kcal_per_100g": "energy-kcal_100g",
    "protein_per_100g": "proteins_100g",
    "carb_per_100g": "carbohydrates_100g",
    "fat_per_100g": "fat_100g",
}


def normalize_barcode(code: str | None) -> str | None:
    """
    Barcodes are compared as digit strings with leading zeros stripped. A UPC-A on a
    US package and the EAN-13 for the same product differ only by a leading zero, so
    without this the two catalogs would never match.
    """
    if not code:
        return None
    digits = "".join(ch for ch in str(code) if ch.isdigit())
    if len(digits) < 8:
        return None
    return digits.lstrip("0") or "0"


def open_maybe_gzip(path: str):
    if path.endswith(".gz"):
        return io.TextIOWrapper(gzip.open(path, "rb"), encoding="utf-8", errors="replace")
    return open(path, newline="", encoding="utf-8", errors="replace")


def read_off(path: str, wanted: set[str], progress_every: int = 500_000) -> dict[str, dict]:
    """
    Stream the OFF export, keeping only barcodes we actually hold. The export is ~9 GB
    uncompressed, so it is never loaded into memory as a whole.
    """
    found: dict[str, dict] = {}
    with open_maybe_gzip(path) as fh:
        reader = csv.DictReader(fh, delimiter="\t")
        if reader.fieldnames and "code" not in reader.fieldnames:
            fh.seek(0)
            reader = csv.DictReader(fh)  # fall back to comma-separated

        for index, row in enumerate(reader, start=1):
            if index % progress_every == 0:
                print(f"    scanned {index:,} OFF rows, matched {len(found):,}…",
                      file=sys.stderr)
            code = normalize_barcode(row.get("code"))
            if not code or code not in wanted or code in found:
                continue
            found[code] = {
                "product_name": (row.get("product_name") or "").strip(),
                **{ours: to_float(row.get(theirs)) for ours, theirs in OFF_COLUMNS.items()},
            }
            if len(found) == len(wanted):
                break
    return found


def percent_difference(ours: float, theirs: float) -> float:
    larger = max(abs(ours), abs(theirs))
    return 0.0 if larger == 0 else abs(ours - theirs) / larger * 100.0


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("catalog_csv", help="CSV from GET /api/FoodCatalog/export")
    ap.add_argument("off_export", help="en.openfoodfacts.org.products.csv[.gz]")
    ap.add_argument("--out", default="off-flags.csv")
    ap.add_argument("--limit", type=int, default=0)
    args = ap.parse_args()

    rows = load_catalog(args.catalog_csv, args.limit)
    by_barcode: dict[str, dict] = {}
    for row in rows:
        code = normalize_barcode(row.get("gtin_upc"))
        if code:
            by_barcode.setdefault(code, row)

    if not by_barcode:
        sys.exit("no barcodes in the catalog export — re-import branded foods so gtin_upc is populated")

    print(f"{len(by_barcode)} catalog products carry a barcode; scanning the OFF export…",
          file=sys.stderr)
    off = read_off(args.off_export, set(by_barcode))

    stats, proposals = CompareStats(rows=len(by_barcode)), []
    today = date.today().isoformat()

    for code, row in by_barcode.items():
        theirs = off.get(code)
        if not theirs:
            stats.unmatched += 1
            continue

        stats.matched += 1
        differences = diff_row(row, {k: theirs.get(k) for k in COMPARED_COLUMNS})
        if not differences:
            stats.agreed += 1
            continue

        stats.differing += 1
        for d in differences:
            stats.by_column[d.column] = stats.by_column.get(d.column, 0) + 1

            if d.ours is None:
                stats.missing_locally += 1
                detail = ("Open Food Facts holds a value for this nutrient and we hold none.")
            else:
                # Describe the disagreement WITHOUT reproducing the OFF value: the size
                # of the gap is our own derived observation, not their data.
                detail = (f"Open Food Facts disagrees with our {d.label} by "
                          f"{percent_difference(d.ours, d.theirs):.0f}% "
                          f"(ours: {d.ours:g} per 100 g).")

            proposals.append(proposal(
                action="flag",                       # never an update: OFF is a signal
                ingredient_id=row.get("ingredient_id", ""),
                fdc_id=row.get("fdc_id", ""),
                field_name="",                       # flags carry no field, so no nutrient write
                current_value=d.ours,
                proposed_value=None,                 # deliberately empty — no OFF value copied
                confidence="0.40",
                reason=(f"{detail} Crowd-sourced signal only — verify against the "
                        f"manufacturer's published label before changing anything. "
                        f"Barcode {code}."),
                source=f"review:off/{today}"))

    write_proposals(args.out, proposals)
    print_stats("Open Food Facts comparison (signal only)", stats, proposals)
    print("All output is flags for human review; no OFF value is copied into the catalog.",
          file=sys.stderr)


if __name__ == "__main__":
    main()

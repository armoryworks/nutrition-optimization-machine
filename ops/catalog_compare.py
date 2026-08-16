"""
Shared comparison core for catalog cross-checkers.

Both checkers answer the same question — "does an outside source agree with what we
stored?" — but they are allowed to do very different things about the answer, because
their sources carry very different authority:

  * USDA FDC is the *origin* of our FDC-sourced rows and is CC0. If it now says
    something different, our copy is simply stale, so a difference is proposed as a
    numeric `update` with an `fdc:` source (authoritative under ProposalPolicy).

  * Open Food Facts is crowd-sourced and licensed ODbL. We use it strictly as a
    *signal*: a difference becomes a `flag` for a human, and OFF's values are never
    copied into the catalog or into the proposal's proposed_value. That keeps the
    share-alike surface at zero and avoids importing crowd data as fact.

Everything compared here is already per 100 g.
"""

from __future__ import annotations

import csv
import sys
from dataclasses import dataclass, field

sys.path.insert(0, __file__.rsplit("/", 1)[0])
from nutrition_normalize import agrees  # noqa: E402

# Catalog export column -> human label used in proposal text.
COMPARED_COLUMNS = {
    "kcal_per_100g": "calories",
    "protein_per_100g": "protein",
    "carb_per_100g": "carbohydrate",
    "fat_per_100g": "fat",
}


@dataclass
class Difference:
    column: str
    ours: float | None
    theirs: float
    label: str


@dataclass
class CompareStats:
    rows: int = 0
    matched: int = 0
    unmatched: int = 0
    agreed: int = 0
    differing: int = 0
    missing_locally: int = 0
    by_column: dict[str, int] = field(default_factory=dict)


def to_float(value) -> float | None:
    if value in (None, "", "NULL"):
        return None
    try:
        return float(value)
    except (TypeError, ValueError):
        return None


def diff_row(ours: dict, theirs: dict[str, float | None]) -> list[Difference]:
    """
    Per-nutrient differences between a catalog row and an outside reading.
    Both sides must already be per 100 g. Values we do not hold are reported too:
    an outside source having a number we lack is worth a human's attention.
    """
    out: list[Difference] = []
    for column, label in COMPARED_COLUMNS.items():
        mine = to_float(ours.get(column))
        yours = theirs.get(column)
        if yours is None:
            continue
        if mine is not None and agrees(mine, yours):
            continue
        out.append(Difference(column=column, ours=mine, theirs=yours, label=label))
    return out


def proposal(*, action: str, ingredient_id: str, fdc_id: str, field_name: str,
             current_value, proposed_value, confidence: str, reason: str,
             source: str) -> dict:
    def fmt(v):
        return "" if v is None else (f"{v:g}" if isinstance(v, (int, float)) else str(v))

    return {
        "action": action,
        "ingredient_id": ingredient_id,
        "fdc_id": fdc_id,
        "field": field_name,
        "current_value": fmt(current_value),
        "proposed_value": fmt(proposed_value),
        "confidence": confidence,
        "reason": reason,
        "source": source,
    }


PROPOSAL_FIELDS = ["action", "ingredient_id", "fdc_id", "field", "current_value",
                   "proposed_value", "confidence", "reason", "source"]


def write_proposals(path: str, proposals: list[dict]) -> None:
    with open(path, "w", newline="", encoding="utf-8") as fh:
        writer = csv.DictWriter(fh, fieldnames=PROPOSAL_FIELDS)
        writer.writeheader()
        writer.writerows(proposals)


def load_catalog(path: str, limit: int = 0) -> list[dict]:
    with open(path, newline="", encoding="utf-8") as fh:
        rows = list(csv.DictReader(fh))
    return rows[:limit] if limit else rows


def print_stats(name: str, stats: CompareStats, proposals: list[dict]) -> None:
    print(f"\n=== {name} ===", file=sys.stderr)
    print(f"catalog rows:      {stats.rows}", file=sys.stderr)
    print(f"matched:           {stats.matched}", file=sys.stderr)
    print(f"unmatched:         {stats.unmatched}", file=sys.stderr)
    print(f"agreed:            {stats.agreed}", file=sys.stderr)
    print(f"differing:         {stats.differing}", file=sys.stderr)
    print(f"we had no value:   {stats.missing_locally}", file=sys.stderr)
    for column, count in sorted(stats.by_column.items(), key=lambda kv: -kv[1]):
        print(f"  {column}: {count}", file=sys.stderr)
    print(f"proposals written: {len(proposals)}", file=sys.stderr)

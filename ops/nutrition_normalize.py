"""
Mass normalization for nutrition read off published labels.

Everything in the NOM catalog is stored **per 100 g**. Published labels are almost
always **per serving** ("240 calories per 1 bar (55 g)"). Comparing those directly is
meaningless — a 55 g bar at 240 kcal is 436 kcal/100 g, and skipping the conversion
would make nearly every product look like a mismatch. So: parse the serving mass,
convert it to grams, and rescale every nutrient by 100/serving_grams before anything
is compared or proposed.

Anything we cannot mass-normalize with confidence is dropped rather than guessed.
Volume units are converted with an assumed density of 1 g/ml, which is only honest
for water-like liquids, so results derived that way are marked `assumed_density`
and are never allowed to drive a numeric change on their own.
"""

from __future__ import annotations

import re
from dataclasses import dataclass, field

# Mass units → grams. Volume units are handled separately because they need a density
# assumption, which we have to disclose rather than bury.
MASS_UNITS: dict[str, float] = {
    "g": 1.0, "gr": 1.0, "gm": 1.0, "gram": 1.0, "grams": 1.0, "gramme": 1.0, "grammes": 1.0,
    "kg": 1000.0, "kilogram": 1000.0, "kilograms": 1000.0,
    "mg": 0.001, "milligram": 0.001, "milligrams": 0.001,
    "oz": 28.349523125, "ounce": 28.349523125, "ounces": 28.349523125,
    "lb": 453.59237, "lbs": 453.59237, "pound": 453.59237, "pounds": 453.59237,
}

VOLUME_UNITS_ML: dict[str, float] = {
    "ml": 1.0, "milliliter": 1.0, "milliliters": 1.0, "millilitre": 1.0, "millilitres": 1.0,
    "cl": 10.0, "dl": 100.0,
    "l": 1000.0, "liter": 1000.0, "liters": 1000.0, "litre": 1000.0, "litres": 1000.0,
    "floz": 29.5735295625, "fl oz": 29.5735295625, "fluid ounce": 29.5735295625,
    "fluid ounces": 29.5735295625,
}

# A serving outside this range is not a serving; refuse rather than scale by nonsense.
MIN_SERVING_G = 0.5
MAX_SERVING_G = 2000.0

_NUM = r"(\d+(?:[.,]\d+)?)"
_UNIT_ALTS = "|".join(
    sorted((re.escape(u) for u in list(MASS_UNITS) + list(VOLUME_UNITS_ML)), key=len, reverse=True)
)
# "55 g", "1.5oz", "240 ml" — unit may be attached or spaced.
_QTY_UNIT_RE = re.compile(rf"{_NUM}\s*({_UNIT_ALTS})\b", re.IGNORECASE)
# Prefer a parenthesised mass: "1 bar (55 g)" is the real serving mass.
_PAREN_RE = re.compile(r"\(([^)]*)\)")
_LEADING_NUM_RE = re.compile(rf"^\s*{_NUM}")


@dataclass
class ServingMass:
    grams: float
    assumed_density: bool = False
    raw: str = ""


@dataclass
class NormalizedNutrition:
    """Per-100 g values plus how we got there."""
    kcal: float | None = None
    protein_g: float | None = None
    carb_g: float | None = None
    fat_g: float | None = None
    serving_grams: float | None = None
    assumed_density: bool = False
    notes: list[str] = field(default_factory=list)

    def as_dict(self) -> dict[str, float | None]:
        return {
            "kcal_per_100g": self.kcal,
            "protein_per_100g": self.protein_g,
            "carb_per_100g": self.carb_g,
            "fat_per_100g": self.fat_g,
        }


def _to_float(text: str) -> float | None:
    try:
        return float(text.replace(",", "."))
    except (TypeError, ValueError):
        return None


def parse_quantity(text: str | float | int | None) -> float | None:
    """
    First number in a label value: "240 calories" -> 240.0, "8 g" -> 8.0, "<1 g" -> 1.0.
    Returns None when there is no number to read (never guesses).
    """
    if text is None:
        return None
    if isinstance(text, (int, float)):
        return float(text)
    m = re.search(_NUM, str(text))
    return _to_float(m.group(1)) if m else None


def parse_serving_mass(text: str | None) -> ServingMass | None:
    """
    Serving mass in grams from a label serving size.

        "55 g"                -> 55.0
        "1 bar (55 g)"        -> 55.0      (parenthesised mass wins)
        "2 tbsp (32g)"        -> 32.0
        "1.5 oz"              -> 42.5
        "1 cup (240 ml)"      -> 240.0     (assumed_density=True)
        "1 bar"               -> None      (no mass — refuse)

    Returns None when no usable mass is present, or when the mass is outside the
    plausible range for a serving.
    """
    if not text:
        return None
    raw = str(text).strip()

    # Parenthesised value first, then the whole string.
    candidates: list[str] = [m.group(1) for m in _PAREN_RE.finditer(raw)]
    candidates.append(raw)

    for candidate in candidates:
        for m in _QTY_UNIT_RE.finditer(candidate):
            value = _to_float(m.group(1))
            unit = m.group(2).lower().replace(".", "")
            if value is None:
                continue
            if unit in MASS_UNITS:
                grams = value * MASS_UNITS[unit]
                assumed = False
            elif unit in VOLUME_UNITS_ML:
                grams = value * VOLUME_UNITS_ML[unit]  # 1 g/ml
                assumed = True
            else:
                continue
            if MIN_SERVING_G <= grams <= MAX_SERVING_G:
                return ServingMass(grams=grams, assumed_density=assumed, raw=raw)
    return None


def is_per_100g(serving_text: str | None) -> bool:
    """True when the panel is already expressed per 100 g (common outside the US)."""
    if not serving_text:
        return False
    normalized = re.sub(r"\s+", "", str(serving_text).lower())
    return normalized.startswith("100g") or normalized in {"100g", "per100g", "100grams"}


def scale_to_100g(value: float | None, serving_grams: float) -> float | None:
    """Rescale one per-serving value onto the 100 g basis."""
    if value is None:
        return None
    if serving_grams <= 0:
        return None
    return value * 100.0 / serving_grams


def normalize(
    *,
    serving_size: str | None,
    kcal: str | float | None = None,
    protein: str | float | None = None,
    carb: str | float | None = None,
    fat: str | float | None = None,
) -> NormalizedNutrition | None:
    """
    Convert a per-serving label panel to per-100 g. Returns None when the serving mass
    cannot be determined — without it there is no honest way to compare against a
    per-100 g catalog, and inventing one would silently corrupt every comparison.
    """
    result = NormalizedNutrition()

    values = {
        "kcal": parse_quantity(kcal),
        "protein_g": parse_quantity(protein),
        "carb_g": parse_quantity(carb),
        "fat_g": parse_quantity(fat),
    }
    if all(v is None for v in values.values()):
        return None

    if is_per_100g(serving_size):
        result.serving_grams = 100.0
        result.notes.append("panel already per 100 g")
        for key, value in values.items():
            setattr(result, key, value)
        return result

    mass = parse_serving_mass(serving_size)
    if mass is None:
        return None

    result.serving_grams = mass.grams
    result.assumed_density = mass.assumed_density
    if mass.assumed_density:
        result.notes.append("volume serving converted at 1 g/ml (assumed density)")
    result.notes.append(f"scaled from {mass.grams:g} g serving to 100 g basis")

    for key, value in values.items():
        setattr(result, key, scale_to_100g(value, mass.grams))
    return result


def agrees(a: float | None, b: float | None, *, rel_tol: float = 0.15, abs_tol: float = 2.0) -> bool:
    """
    Whether two per-100 g values agree closely enough to be called the same number.
    Rounding on labels is coarse, so a relative tolerance with a small absolute floor
    avoids flagging 0.4 g vs 0.5 g of fat as a discrepancy.
    """
    if a is None or b is None:
        return False
    if abs(a - b) <= abs_tol:
        return True
    larger = max(abs(a), abs(b))
    return larger > 0 and abs(a - b) / larger <= rel_tol

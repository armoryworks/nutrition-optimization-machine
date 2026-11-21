#!/usr/bin/env python3
"""Freezes the container-size facts the golden set container-facts.tsv holds.

Reads the USDA FDC Branded Foods files (branded_food.csv, food.csv) and the SR Legacy
food_portion.csv on this machine, never a network. For every class of
nom-api/Nom.Orch/Containers/container-classes.tsv it writes the parsed package sizes of the
class's products (one per GTIN, latest available_date, United States, not discontinued,
no multi-pack), and for every FDC id given with --portions the container portions SR Legacy
lists for it. It is an implementation of the rule written apart from the C# code, so the
golden set compares two readings of the same files.

usage: container_facts.py <branded-dir> <sr-legacy-dir> <classes.tsv> --portions <ids> > container-facts.tsv
"""
import argparse
import collections
import csv
import re
import sys

csv.field_size_limit(10 ** 9)

OZ = 28.349523125
LB = 453.59237
FL_OZ = 29.5735295625
UNITS = [
    (r"(fl\.?\s*oz\.?|floz|oza)", "ml", FL_OZ),
    (r"(oz\.?|onz|nz|ounces?)", "g", OZ),
    (r"(lbs?\.?|lbr|pounds?)", "g", LB),
    (r"(kg|kgm|kilograms?)", "g", 1000.0),
    (r"(g|grm|grams?|gr)", "g", 1.0),
    (r"(ml|mlt|milliliters?)", "ml", 1.0),
    (r"(l|ltr|liters?|litres?)", "ml", 1000.0),
    (r"(pt|pints?)", "ml", 473.176473),
    (r"(qt|qts|quarts?)", "ml", 946.352946),
    (r"(gal|gll|gallons?)", "ml", 3785.411784),
]
MULTI = re.compile(r"(\d\s*[x×]\s*\d|\bct\b|\bcount\b|\bpk\b|\bpack\b|\bpacks\b|\bservings?\b|\bea\b|\beach\b)", re.I)


def package_weight(text):
    """(grams, millilitres) of a package_weight string; None where it cannot be read or is a multi-pack."""
    if not text or not text.strip() or MULTI.search(text):
        return None
    grams = ml = None
    grams_exact = ml_exact = False
    for segment in text.split("/"):
        s = segment.strip().lower()
        m = re.fullmatch(r"(\d+(?:\.\d+)?|\.\d+)\s*(.+?)\.?", s)
        if not m or float(m.group(1)) <= 0:
            continue
        value = float(m.group(1))
        unit = m.group(2).strip()
        for pattern, kind, factor in UNITS:
            if re.fullmatch(pattern, unit):
                exact = factor in (1.0, 1000.0)
                if kind == "g" and (grams is None or (exact and not grams_exact)):
                    grams, grams_exact = value * factor, exact
                if kind == "ml" and (ml is None or (exact and not ml_exact)):
                    ml, ml_exact = value * factor, exact
                break
    if grams is None and ml is None:
        return None
    return (round(grams, 1) if grams else None, round(ml, 1) if ml else None)


def singular(word):
    if len(word) > 4 and word.endswith("ies"):
        return word[:-3] + "y"
    if len(word) > 4 and word.endswith("oes"):
        return word[:-2]
    if len(word) > 3 and word.endswith("s") and not word.endswith("ss"):
        return word[:-1]
    return word


def words(text):
    return {singular(w) for w in re.findall(r"[a-z0-9%]+", text.lower())}


def matches(spec, tokens):
    if spec in ("", "-"):
        return True
    return all(any(singular(alt) in tokens for alt in item.split("|")) for item in spec.split(","))


def excluded(spec, tokens):
    if spec in ("", "-"):
        return False
    return any(singular(w) in tokens for w in spec.split(","))


def read_classes(path):
    with open(path, encoding="utf-8", newline="") as f:
        return list(csv.DictReader(f, delimiter="\t", quoting=csv.QUOTE_NONE))


def scan(branded, classes):
    categories = {}
    for c in classes:
        for cat in c["brandedCategories"].split("|"):
            categories.setdefault(cat, []).append(c)
    products = {}
    with open(f"{branded}/branded_food.csv", encoding="utf-8", newline="") as f:
        for r in csv.DictReader(f):
            if r["branded_food_category"] not in categories:
                continue
            if r["market_country"] != "United States" or r["discontinued_date"]:
                continue
            products[r["fdc_id"]] = r
    descriptions = {}
    with open(f"{branded}/food.csv", encoding="utf-8", newline="") as f:
        for r in csv.DictReader(f):
            if r["fdc_id"] in products:
                descriptions[r["fdc_id"]] = r["description"]
    latest = {}
    for fdc, r in products.items():
        key = r["gtin_upc"].lstrip("0") or fdc
        if key not in latest or (r["available_date"], int(fdc)) > (latest[key][0]["available_date"], int(latest[key][1])):
            latest[key] = (r, fdc)
    sizes = collections.defaultdict(collections.Counter)
    for r, fdc in latest.values():
        tokens = words(descriptions.get(fdc, ""))
        size = package_weight(r["package_weight"])
        if size is None:
            continue
        for c in categories[r["branded_food_category"]]:
            if not matches(c["productAll"], tokens) or excluded(c["productNone"], tokens):
                continue
            value = size[0] if c["basis"] == "g" else size[1]
            if value:
                sizes[c["id"]][int(round(value))] += 1
    return sizes


def portions(sr, ids):
    out = collections.defaultdict(list)
    with open(f"{sr}/food_portion.csv", encoding="utf-8", newline="") as f:
        for r in csv.DictReader(f):
            if r["fdc_id"] in ids:
                out[r["fdc_id"]].append(r)
    names = {}
    with open(f"{sr}/food.csv", encoding="utf-8", newline="") as f:
        for r in csv.DictReader(f):
            if r["fdc_id"] in ids:
                names[r["fdc_id"]] = (r["description"], r["food_category_id"])
    return out, names


def clean(text):
    return re.sub(r"[\t\r\n|;]+", " ", text).strip()


def main():
    p = argparse.ArgumentParser()
    p.add_argument("branded")
    p.add_argument("sr")
    p.add_argument("classes")
    p.add_argument("--portions", default="")
    a = p.parse_args()
    classes = read_classes(a.classes)
    print("id\tkind\tdescription\tcategoryId\tfacts")
    sizes = scan(a.branded, classes)
    for c in classes:
        hist = ";".join(f"{g}:{n}" for g, n in sorted(sizes[c["id"]].items()))
        print(f"class:{c['id']}\tbranded\t-\t-\t{hist or '-'}")
    ids = {i for i in re.split(r"[\s,]+", a.portions) if i}
    rows, names = portions(a.sr, ids)
    for fdc in sorted(ids, key=int):
        desc, cat = names.get(fdc, ("-", "-"))
        facts = ";".join(
            f"{r['seq_num']}|{r['amount']}|{clean(r['modifier']) or '-'}|{r['gram_weight']}" for r in rows.get(fdc, []))
        print(f"fdc:{fdc}\tportions\t{clean(desc)}\t{cat}\t{facts or '-'}")


if __name__ == "__main__":
    main()

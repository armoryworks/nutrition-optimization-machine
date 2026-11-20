#!/usr/bin/env python3
import argparse
import csv
import html
import json
import pickle
import re
import sys
from pathlib import Path

JUDGED_PAIRS = "agent-review-2026-11-13"
WORDING_MOVES = "agent-review-2026-11-16"
GOLDEN_GATE = "design:golden-gate"
COUNT_FALLBACK = "design:count-fallback"
WAVE1_COUNT = "report:wave1-count-fallback"
NOT_FIXED = "report:integrate-not-fixed"
COMMIT_D41EEDF = "commit:d41eedf"
AI_DESIGN = "design:ai-design-1"
UNFOLD = "design:unfold"
COMPOUND = "design:compound"
BAD = {"wrong", "near", "compound"}


def read_tsv(path):
    with open(path, encoding="utf-8", newline="") as f:
        return list(csv.DictReader(f, delimiter="\t", quoting=csv.QUOTE_NONE))


def clean(value):
    return re.sub(r"[\t\r\n]+", " ", "" if value is None else str(value)).strip()


def write_tsv(path, header, rows):
    with open(path, "w", encoding="utf-8", newline="") as f:
        f.write("\t".join(header) + "\n")
        for row in rows:
            f.write("\t".join(clean(row.get(h, "")) for h in header) + "\n")
    print(f"{path.name}: {len(rows)} rows", file=sys.stderr)


LINES = [
    ("cited-01", "leading-number", "1 teaspoon salt", "1", "Teaspoon", "*", "salt", "salt", GOLDEN_GATE, "example 1"),
    ("cited-02", "unicode-fraction", "½-pint bread crumbs", "1", "Cup", "*", "bread crumbs", "bread crumbs", GOLDEN_GATE, "example 2"),
    ("cited-03", "unicode-fraction", "³⁄₄ cup butter", "0.75", "Cup", "*", "butter", "butter", GOLDEN_GATE, "example 3"),
    ("cited-04", "range", "1-1/2 cups flour", "1.5", "Cup", "*", "flour", "flour", GOLDEN_GATE, "example 4"),
    ("cited-05", "number-word", "a Gallon of new Milk", "16", "Cup", "*", "new Milk", "*", GOLDEN_GATE, "example 5"),
    ("cited-06", "trailing-or-inner-number", "rich boiling cream (1/4 pint)", "0.5", "Cup", "*", "rich boiling cream", "*", GOLDEN_GATE, "example 6"),
    ("cited-07", "html-entity", "&frac14; to &frac12; ounce honey simple syrup", "0.25", "Ounce", "*", "honey simple syrup", "*", GOLDEN_GATE, "example 7"),
    ("cited-08", "no-number", "a little salt", "-", "-", "*", "salt", "salt", GOLDEN_GATE, "example 8"),
    ("cited-09", "pack-parenthetical", "1 (14-ounce) can sweetened condensed milk", "*", "*", "14 Ounce", "*", "*", GOLDEN_GATE, "example 9: the pack weight is the amount"),
    ("cited-10", "pack-parenthetical", "2 15oz. cans black beans ($1.18)", "*", "*", "30 Ounce", "black beans", "black beans", GOLDEN_GATE, "example 10: parse and name"),
    ("cited-11", "number-word", "two drachms of saffron", "*", "*", "*", "saffron", "saffron", GOLDEN_GATE, "example 11: a drachm is a mass, not a piece"),
    ("cited-12", "trailing-or-inner-number", "Chocolate, 2 squares", "2", "*", "*", "Chocolate", "Chocolate", GOLDEN_GATE, "example 12"),
    ("cited-14", "attached-metric", "60g cheddar cheese, shredded", "60", "Gram", "*", "cheddar cheese, shredded", "cheddar cheese", GOLDEN_GATE, "example 14: the wording should drop the amount"),
    ("cited-15", "range", "1/2 to 3/4 cup pistachios, chopped roughly", "0.5", "Cup", "*", "*", "pistachios", GOLDEN_GATE, "example 15: the bare name drops the preparation"),
    ("cited-16", "italian", "Burro fresco, 185 grammi", "185", "Gram", "*", "*", "*", GOLDEN_GATE, "example 16"),
    ("size-01", "container", "1 13oz. can coconut milk ($1.99)", "*", "*", "13 Ounce", "*", "*", COUNT_FALLBACK, "sized lines: 369 g"),
    ("size-02", "container", "1 15-oz can chickpeas, rinsed and drained", "*", "*", "15 Ounce", "*", "*", COUNT_FALLBACK, "sized lines: 425 g"),
    ("size-03", "container", "1 14 ounce can sweetened condensed milk", "*", "*", "14 Ounce", "*", "*", COUNT_FALLBACK, "sized lines: 397 g"),
    ("size-04", "container", "2 (15-oz.) cans white beans, drained and rinsed", "*", "*", "30 Ounce", "*", "*", COUNT_FALLBACK, "sized lines: 850 g"),
    ("size-05", "container", "1 15oz. can diced tomatoes ($1.00)", "*", "*", "15 Ounce", "*", "*", COUNT_FALLBACK, "sized lines: 425 g"),
    ("size-06", "container", "1 (12 ounce) can Evaporated Milk", "*", "*", "12 Ounce", "*", "*", COUNT_FALLBACK, "sized lines: 340 g"),
    ("size-07", "container", "1 (24 ounce) jar pasta sauce", "*", "*", "24 Ounce", "*", "*", COUNT_FALLBACK, "sized lines: 680 g"),
    ("size-08", "container", "two 14-ounce cans fire-roasted tomatoes", "*", "*", "28 Ounce", "*", "*", COUNT_FALLBACK, "on an item weight: 794 g"),
    ("size-09", "container", "1 (4-ounce) can green chiles", "*", "*", "4 Ounce", "*", "*", COUNT_FALLBACK, "on an item weight"),
    ("size-10", "container", "1 large can (28 ounces) diced tomatoes", "*", "*", "28 Ounce", "*", "*", COUNT_FALLBACK, "on an item weight: 794 g"),
    ("size-11", "container", "1 (28-ounce) can diced tomatoes", "*", "*", "28 Ounce", "*", "*", GOLDEN_GATE, "example 30"),
    ("size-12", "container", "1 (5-ounce) package smoked salmon", "*", "*", "5 Ounce", "*", "*", GOLDEN_GATE, "example 39"),
    ("size-13", "container", "1/2 can (about 8 ounces) tomato sauce", "*", "*", "8 Ounce", "*", "*", COMMIT_D41EEDF, "a size after 'about' is the amount of the whole line"),
    ("size-14", "container", "2 cans (about 30 ounces) black beans", "*", "*", "30 Ounce", "*", "*", COMMIT_D41EEDF, "a size after 'about' is the amount of the whole line"),
    ("size-15", "container", "2 cans (28 ounces) full fat coconut milk", "*", "*", "28 Ounce", "*", "*", NOT_FIXED, "28 is the total for two cans; still read as 56"),
    ("size-16", "container", "1 1/2 (180g) cups all-purpose flour", "*", "*", "180 Gram", "*", "*", COUNT_FALLBACK, "sized lines: 180 g"),
    ("unsized-01", "container", "one can of tomatoes", "1", "Can", "-", "*", "*", COUNT_FALLBACK, "container with no size"),
    ("unsized-02", "container", "1/4 can of mushrooms", "0.25", "Can", "-", "*", "*", COUNT_FALLBACK, "container with no size"),
    ("unsized-03", "container", "1 can of corn", "1", "Can", "-", "*", "*", COUNT_FALLBACK, "container with no size"),
    ("unsized-04", "container", "one can of salmon", "1", "Can", "-", "*", "*", COUNT_FALLBACK, "container with no size"),
    ("unsized-05", "container", "1 can coconut milk", "1", "Can", "-", "*", "*", COUNT_FALLBACK, "container with no size"),
    ("unsized-06", "container", "½ can sweetened condensed milk", "0.5", "Can", "-", "*", "*", COUNT_FALLBACK, "container with no size"),
    ("unsized-07", "container", "2 packages lime flavored gelatin", "2", "Package", "-", "*", "*", COUNT_FALLBACK, "container with no size"),
    ("unsized-08", "container", "1 tall can of Carnation Milk", "1", "Can", "-", "*", "*", WAVE1_COUNT, "still read as Piece"),
    ("unsized-09", "container", "1 family size package golden oreos", "1", "Package", "-", "*", "*", WAVE1_COUNT, "still read as Piece"),
    ("alone-01", "container", "4 (6-ounce) salmon filets", "*", "*", "-", "*", "*", COUNT_FALLBACK, "lines the size rules leave alone"),
    ("alone-02", "container", "40 (½ pound) marshmallows.", "*", "*", "-", "*", "*", COUNT_FALLBACK, "lines the size rules leave alone"),
    ("alone-03", "container", "8 medium (8-ounces) shrimp", "*", "*", "-", "*", "*", COUNT_FALLBACK, "lines the size rules leave alone"),
    ("alone-04", "container", "1 can or ½ lb. fresh tomatoes", "*", "*", "-", "*", "*", COUNT_FALLBACK, "lines the size rules leave alone"),
    ("alone-05", "container", "1 can (#2 size or 2 1/2 cups) pineapple tidbits", "*", "*", "-", "*", "*", COUNT_FALLBACK, "lines the size rules leave alone"),
    ("alone-06", "container", "1 914-ounce) can coconut cream", "*", "*", "-", "*", "*", COUNT_FALLBACK, "lines the size rules leave alone"),
]

WORDINGS = [
    ("cited-17", "plain", "416", "fresh lime juice", "fresh lime juice", "fresh lime juice", "*", GOLDEN_GATE, "example 17"),
    ("cited-20", "leading-word-residue", "147", "and half", "*", "*", "yes", GOLDEN_GATE, "example 20: residue"),
    ("cited-21", "comma-prep", "81", "Eggs, separated", "Eggs, separated", "Eggs", "*", GOLDEN_GATE, "example 21: the bare name drops the preparation"),
    ("cited-22", "unit-word-inside", "10", "half-cupful of butter", "butter", "butter", "*", GOLDEN_GATE, "example 22"),
]

LINE_KINDS = [
    ("cited-23a", "buttered paper", "equipment", GOLDEN_GATE, "example 23"),
    ("cited-23b", "For the Gravy:", "note", GOLDEN_GATE, "example 23"),
    ("cited-23c", "salt + salt", "artefact", GOLDEN_GATE, "example 23"),
    ("cited-24a", "lemon wedges (for serving)", "-", GOLDEN_GATE, "example 24: no pre-pass answer"),
    ("cited-24b", "four cloves of garlic + four cloves of garlic", "-", GOLDEN_GATE, "example 24: a real ingredient"),
    ("ai-01", "rich puff paste", "-", AI_DESIGN, "change 1: an ordinary food"),
    ("ai-02", "chicken-stock", "-", AI_DESIGN, "change 1: an ordinary food"),
    ("ai-03", "hot mashed potatoes", "-", AI_DESIGN, "change 1: an ordinary food"),
    ("ai-04", "whites 3 eggs beaten stiff", "-", AI_DESIGN, "change 1: an ordinary food"),
]

VETO = [
    ("ai-01", "rich puff paste", "reference", "0", "yes", "yes", "refused", AI_DESIGN, "change 1: an ordinary food labelled not an ingredient"),
    ("ai-02", "chicken-stock", "reference", "0", "yes", "yes", "refused", AI_DESIGN, "change 1: an ordinary food labelled not an ingredient"),
    ("ai-03", "hot mashed potatoes", "reference", "0", "yes", "yes", "refused", AI_DESIGN, "change 1: an ordinary food labelled not an ingredient"),
    ("ai-04", "whites 3 eggs beaten stiff", "instruction", "0", "yes", "yes", "refused", AI_DESIGN, "change 1: an ordinary food labelled not an ingredient"),
    ("rule-01", "1 cup flour", "note", "1", "yes", "no", "states an amount", "rule:veto", "a line with an amount is an ingredient"),
    ("rule-02", "2 tablespoons butter", "equipment", "2", "no", "no", "states an amount", "rule:veto", "a line with an amount is an ingredient"),
    ("rule-03", "Butter", "equipment", "0", "yes", "no", "names a curated food", "rule:veto", "exactly a curated food's name"),
    ("rule-04", "flour", "reference", "0", "no", "no", "names a curated food", "rule:veto", "exactly a curated food's name"),
    ("rule-05", "buttered paper", "equipment", "0", "no", "no", "-", "rule:veto", "not a food, no amount, not on a curated food"),
    ("rule-06", "For the Gravy:", "note", "0", "no", "yes", "-", "rule:veto", "a heading off any curated food stands"),
    ("rule-07", "1 cup flour", "ingredient", "1", "yes", "yes", "-", "rule:veto", "an ingredient label is never refused"),
]

COMPOUND_CLASSES = {
    "pair": "Split", "each": "Split", "and": "Split", "part-pair": "Split",
    "or": "PickAlternative", "or-head": "PickAlternative", "or-first": "PickAlternative",
    "or-last": "Leave", "or?": "Leave", "or-ambig": "Leave", "prep": "*", "same": "*", "protected": "Leave",
    "and?": "Leave", "mixed": "Leave",
}

COMPOUND_SAMPLES = [
    ("sample-01", "Salt and pepper to taste ($0.05)", "Split", "and-salt-pepper", "Salt|Pepper"),
    ("sample-02", "a little pepper and salt", "Split", "and-salt-pepper", "Pepper|Salt"),
    ("sample-03", "1/2 tsp each sea salt and black pepper ((plus more to taste))", "Split", "*", "Salt|Pepper"),
    ("sample-04", "1/4 cup each of canned peas, green beans, and tomatoes", "Split", "and-each", "*"),
    ("sample-05", "grated rind and juice of 1/2 lemon", "Split", "part-pair", "Lemon Peel|Lemon Juice"),
    ("sample-06", "1 pint of boiling milk, or 1 pint of boiling cream", "PickAlternative", "*", "Milk"),
]

UNFOLD_LINES = [
    (34986, "1 3/4 cup granulated sugar + 1/3 cup granulated sugar, (for rolling cookies)", "2", "example 1"),
    (61682, "¼ tsp salt ($0.01) + 1 pinch salt ($0.01)", "2", "example 2"),
    (60811, "½ teaspoon sea salt + ½ teaspoon sea salt + ½ teaspoon sea salt + ¼ teaspoon sea salt", "4", "example 3"),
    (98371, "1 tablespoonful of butter + 1/4 pound of butter + 1/4 pound of butter + 1/4 pound of butter + 1/4 pound of butter + 1 pound of butter", "6", "example 4: no source list"),
    (93258, "flour + 2 quarts flour", "2", "example 5: no source list"),
    (54546, "One tablespoon of grated onion + One onion, chopped fine", "2", "example 6"),
    (36088, "2 tablespoons extra-virgin olive oil + Extra-virgin olive oil (for drizzling)", "2", "example 7"),
    (61606, "kosher salt and pepper + kosher salt and pepper", "2", "example 8: two segments, collapsed later"),
    (70179, "sugar + sugar", "2", "example 8: two segments, collapsed later"),
    (33036, "3  large eggs + 1  large egg + 1 teaspoon water, (lightly beaten for egg wash)", "2", "example 9: the source list keeps egg and water together"),
    (61764, "1 cup + 2 Tablespoons (225g) granulated sugar", "1", "example 10: one source line, not a fold"),
    (31922, "1  large egg + 1 egg white", "1", "example 10: one source line, not a fold"),
    (63051, "salt + pepper (to taste)", "1", "example 10: one source line, not a fold"),
    (83024, "3/4 c + 2 T milk", "-", "example 11: no source list and a bare-amount part"),
    (23963, "1/2 cup unsalted butter (softened) + 1 cup butter, (melted and slightly cooled)", "2", "example 12"),
    (40675, "1 and 1/2 cups (135g) shredded mozzarella cheese + 1/2 cup (45g) shredded mozzarella cheese", "2", "example 12"),
    (35658, "1 teaspoon rosemary + 1/2 teaspoon fresh rosemary", "2", "example 12"),
    (40785, "3 tablespoons fresh lemon juice + 1 tablespoon lemon zest", "2", "example 13"),
    (94292, "2 ounces ground mustard + 1 pint black mustard-seed + 1 ounce cloves", "3", "example 14"),
    (126506, "one-half pint cold water + One-half box Coxe’s gelatine", "2", "example 14"),
    (141734, "3 cloves garlic (unpeeled) + 2 cloves garlic (minced)", "2", "example 16"),
]

LINK_EXAMPLES = [
    ("cited-25", "chopped onion", "Yellow Onion", "=Onion", "292", GOLDEN_GATE, "example 25: rule, 11-13"),
    ("cited-26", "quarter of a pound of butter", "Light Unsalted Butter", "=Butter", "57", GOLDEN_GATE, "example 26: rule"),
    ("cited-27", "white onions", "Onion", "=White Onions", "24", GOLDEN_GATE, "example 27: rule"),
    ("cited-28", "four ounces of ground rice", "Medium-Grain White Rice", "~Rice Flour", "3", GOLDEN_GATE, "example 28: reviewed move, 11-17"),
    ("cited-29", "whole pepper", "Banana Peppers", "~Pepper", "165", GOLDEN_GATE, "example 29: reviewed move; the rule says stay"),
    ("cited-30", "1 (28-ounce) can diced tomatoes", "Canned Diced Tomatoes", "!Tomatoes", "121", GOLDEN_GATE, "example 30: moved to fresh tomatoes, wrong"),
    ("cited-31", "A can of salmon", "Canned Salmon", "!Salmon", "10", GOLDEN_GATE, "example 31: moved to fresh salmon, wrong"),
    ("cited-33", "dry onion", "Yellow Onion", "!Onion", "7", GOLDEN_GATE, "example 33: refuted"),
]


def key(line):
    return re.sub(r"\s+", " ", html.unescape(line or "")).strip().lower()


def parts(line):
    out, start = [], 0
    while True:
        at = line.find(" + ", start)
        end = len(line) if at < 0 else at
        s, e = start, end
        while s < e and line[s].isspace():
            s += 1
        while e > s and line[e - 1].isspace():
            e -= 1
        out.append((s, e))
        if at < 0:
            return out
        start = at + 3


def source_list(raw_json_ld):
    try:
        doc = json.loads(raw_json_ld)
    except (TypeError, ValueError):
        return set()
    items = doc.get("recipeIngredient") if isinstance(doc, dict) else None
    return {key(i) for i in items if isinstance(i, str) and key(i)} if isinstance(items, list) else set()


def build_lines(out):
    header = ["id", "stratum", "raw", "quantity", "measurement", "size", "wording", "bare", "parse", "name", "source", "note"]
    rows = []
    for (i, stratum, raw, qty, unit, size, wording, bare, source, note) in LINES:
        parse_judged = any(v != "*" for v in (qty, unit, size))
        name_judged = any(v != "*" for v in (wording, bare))
        rows.append(dict(id=i, stratum=stratum, raw=raw, quantity=qty, measurement=unit, size=size, wording=wording, bare=bare,
                         parse="new" if parse_judged else "-", name="new" if name_judged else "-", source=source, note=note))
    write_tsv(out / "lines.tsv", header, rows)


def build_wordings(out):
    header = ["id", "stratum", "lines", "name", "normalized", "bare", "residue", "status", "source", "note"]
    rows = [dict(id=i, stratum=s, lines=n, name=name, normalized=norm, bare=bare, residue=res, status="new", source=src, note=note)
            for (i, s, n, name, norm, bare, res, src, note) in WORDINGS]
    write_tsv(out / "wordings.tsv", header, rows)


def build_line_kinds(out):
    header = ["id", "raw", "kind", "status", "source", "note"]
    write_tsv(out / "line-kinds.tsv", header, [dict(id=i, raw=raw, kind=kind, status="new", source=src, note=note) for (i, raw, kind, src, note) in LINE_KINDS])


def build_veto(out):
    header = ["id", "raw", "kind", "quantity", "onCurated", "wide", "expect", "status", "source", "note"]
    write_tsv(out / "veto.tsv", header, [dict(id=i, raw=raw, kind=k, quantity=q, onCurated=c, wide=w, expect=e, status="new", source=s, note=n)
                                         for (i, raw, k, q, c, w, e, s, n) in VETO])


def build_link_pairs(out, evidence, captures):
    usda = {r["id"]: r for r in read_tsv(captures / "usda.tsv")}
    name_matches = {(r["wording"], r["fdcId"]) for r in read_tsv(captures / "name-matches.tsv")}
    with open(evidence / "judge" / "verdicts.pkl", "rb") as f:
        judged = pickle.load(f)
    header = ["id", "origin", "lines", "foodId", "fdcId", "wording", "description", "verdict", "nameMatch", "judgedBy", "rule", "status", "note"]
    rows = []
    for n, (origin, lines, food_id, wording, description, verdict, note) in enumerate(judged, start=1):
        food = usda.get(food_id)
        fdc = food["fdcId"] if food else "-"
        matched = food is not None and (wording.strip().lower(), fdc) in name_matches
        if verdict == "ok" and food:
            rule = f"~{food_id}"
        elif verdict in BAD and food:
            rule = f"!{food_id}"
        else:
            rule = "*"
        rows.append(dict(id=f"P{n:04d}", origin=origin, lines=lines, foodId=food_id, fdcId=fdc, wording=wording, description=description,
                         verdict=verdict or "-", nameMatch="yes" if matched else "no", judgedBy=JUDGED_PAIRS, rule=rule,
                         status="new" if rule != "*" else "-", note=note if food else (note + " (food no longer in the USDA catalog)").strip()))
    write_tsv(out / "link-pairs.tsv", header, rows)


def build_links(out, evidence, captures):
    catalog = read_tsv(captures / "catalog.tsv")
    by_id = {r["id"]: r for r in catalog}
    live = {r["name"].lower(): r for r in catalog if r["hasNutrition"] == "true"}
    verdicts = json.loads((evidence / "rollout-2026-11-16" / "verdicts.json").read_text(encoding="utf-8"))["s1"]
    header = ["id", "origin", "lines", "raw", "onId", "onFood", "expect", "status", "stays", "source", "note"]
    rows, dropped = [], 0
    for (i, raw, on, expect, lines, src, note) in LINK_EXAMPLES:
        on_row = live.get(on.lower())
        target = live.get(expect[1:].lower())
        if not on_row or not target:
            dropped += 1
            continue
        rows.append(dict(id=i, origin="cited", lines=lines, raw=raw, onId=on_row["id"], onFood=on_row["name"], expect=expect[0] + target["name"],
                         status="new", stays="new" if expect[0] in "=~" else "-", source=src, note=note))
    for v in verdicts:
        raw = v.get("sample") or v.get("wording")
        frm = by_id.get(v["frm"])
        if not raw or not frm:
            dropped += 1
            continue
        if v["state"] == "none":
            expect = "stay"
        else:
            to = by_id.get(v["to"])
            if not to or to["hasNutrition"] != "true":
                dropped += 1
                continue
            expect = ("~" if v["state"] == "agreed" else "!") + to["name"]
        rows.append(dict(id="M" + v["row"], origin="wording-move", lines=v.get("n", ""), raw=raw, onId=frm["id"], onFood=frm["name"], expect=expect,
                         status="new", stays="new" if expect[0] in "=~" else "-", source=WORDING_MOVES, note=v.get("reason", "")))
    write_tsv(out / "links.tsv", header, rows)
    print(f"links: {dropped} judged rows dropped because a food is no longer curated", file=sys.stderr)


def build_compound(out, evidence, captures, lines_for_vocab):
    design = (evidence / "wrapup-2026-11-19" / "cleanup" / "compound.md").read_text(encoding="utf-8")
    table = re.findall(r"^(\d+)\|([^|\n]+)\|([^|\n]+)\|(\d+)\|([^\n]+)$", design, re.M)
    header = ["id", "raw", "kind", "rule", "parts", "status", "source", "note"]
    rows = []
    for n, (count, wording, cls, _, example) in enumerate(table, start=1):
        kind = COMPOUND_CLASSES.get(cls.strip(), "*")
        raw = wording.strip() if example.strip() == "(same)" else example.strip()
        if kind == "*":
            continue
        rows.append(dict(id=f"top-{n:02d}", raw=raw, kind=kind, rule="*", parts="*", status="new", source=COMPOUND,
                         note=f"top wordings: {wording.strip()} ({cls.strip()}, {count} lines)"))
    for (i, raw, kind, rule, prts) in COMPOUND_SAMPLES:
        rows.append(dict(id=i, raw=raw, kind=kind, rule=rule, parts=prts, status="new", source=COMPOUND, note="sample part lines"))
    write_tsv(out / "compound.tsv", header, rows)
    lines_for_vocab.extend(r["raw"] for r in rows)


def build_vocabulary(out, captures, golden_lines):
    texts = [key(html.unescape(l)) for l in golden_lines]
    rows = []
    for r in read_tsv(captures / "vocab.tsv"):
        if r["kind"] == "alias":
            rows.append(dict(kind="alias", id=r["id"], name=r["name"], context=r["context"]))
        elif r["name"].strip() and any(r["name"].strip().lower() in t for t in texts):
            rows.append(dict(kind="known", id=r["id"], name=r["name"], context="-"))
    write_tsv(out / "compound-vocabulary.tsv", ["kind", "id", "name", "context"], rows)


def build_unfold(out, captures):
    captured = {}
    for line in (captures / "unfold.raw").read_text(encoding="utf-8").splitlines()[1:]:
        if line.strip():
            r = json.loads(line)
            captured[r["recipe"]] = r
    header = ["id", "recipe", "raw", "sources", "segments", "status", "source", "note"]
    rows = []
    for n, (recipe, raw, segments, note) in enumerate(UNFOLD_LINES, start=1):
        r = captured.get(recipe)
        if r is None:
            print(f"unfold: recipe {recipe} not on staging", file=sys.stderr)
            continue
        if r["sources"]:
            keys = set().union(*(source_list(s) for s in r["sources"]))
            ps = parts(raw)
            runs = {key(raw[ps[a][0]:ps[b][1]]) for a in range(len(ps)) for b in range(a, len(ps))}
            sources = " ¦ ".join(sorted(k for k in keys if k in runs)) or "{}"
        else:
            sources = "-"
        rows.append(dict(id=f"cited-{n:02d}", recipe=recipe, raw=raw, sources=sources, segments=segments, status="new", source=UNFOLD, note=note))
    write_tsv(out / "unfold.tsv", header, rows)


def build_prose(out, captures):
    header = ["id", "recipe", "description", "steps", "variant", "expect", "status", "source", "note"]
    rows = []
    for line in (captures / "prose.raw").read_text(encoding="utf-8").splitlines()[1:]:
        if not line.strip():
            continue
        r = json.loads(line)
        steps = [clean(t) for t in r["steps"] if clean(t)]
        if any("¦" in t for t in steps):
            continue
        text = " ".join([r["description"]] + steps)
        base = dict(recipe=r["recipe"], description=clean(r["description"]) or "-", steps=" ¦ ".join(steps), status="new", source="rule:rewrite-fidelity")
        rows.append(dict(base, id=f"{r['recipe']}-same", variant="identical", expect="unchanged", note="the source given back as its own rewrite"))
        for i, step in enumerate(steps):
            numbers = re.findall(r"(?<![\d/.,])\d{2,3}(?![\d/.,])", step)
            rest = " ".join([r["description"]] + steps[:i] + steps[i + 1:])
            lone = [n for n in numbers if not re.search(r"(?<![\d/.,])" + n + r"(?![\d/.,])", rest)]
            if lone:
                rows.append(dict(base, id=f"{r['recipe']}-drop", variant=f"drop-step-{i + 1}", expect="lost-number",
                                 note=f"step {i + 1} states {lone[0]}, which no other step or the description states"))
                break
        if not re.search(r"(?<!\d)437(?!\d)", text):
            rows.append(dict(base, id=f"{r['recipe']}-add", variant="add-number", expect="added-number", note="the last step gains 'Rest it 437 minutes.'"))
    write_tsv(out / "prose.tsv", header, rows)


def build_recipes(out, captures):
    recipes = []
    design_counted = {62963: 10, 67498: 11, 41537: 4, 42114: 5}
    design_known = {
        61142: "15 grape or cherry tomatoes weighed as 15 x 123 g; 4 basil leaves as 21 g",
        37855: "a 5-ounce package of smoked salmon weighed as 28.35 g; a sheet of puff pastry as 47 g",
        33365: "the yield '1' is read as one serving",
    }
    for line in (captures / "recipes.raw").read_text(encoding="utf-8").splitlines()[1:]:
        if not line.strip():
            continue
        r = json.loads(line)
        public = r["licence"] == "PublicDomain"
        stored = r.get("stored") or {}
        hand = any(not v["calculated"] for v in stored.values())
        lines = []
        for l in r["lines"]:
            food = l["food"]
            per100 = food.get("per100") or {}
            lines.append(dict(
                qty=l["qty"], unit=l["unit"], kind=l["kind"], raw=l["raw"] if public else None,
                food=dict(id=food["id"], name=food["name"], hasNutrition=food["hasNutrition"], gramsPerItem=food["gramsPerItem"],
                          gramsPerMl=food["gramsPerMl"], referenceServingGrams=food["referenceServingGrams"],
                          per100=dict(calories=per100.get("5035"), protein=per100.get("5006"), fat=per100.get("5000")))))
        expected = dict(
            countedLines=design_counted.get(r["id"]),
            calories=stored.get("5035", {}).get("amount"),
            protein=stored.get("5006", {}).get("amount"),
            fat=stored.get("5000", {}).get("amount"),
            handLabel=hand)
        recipes.append(dict(id=r["id"], origin=r["origin"], licence=r["licence"], name=r["name"] if public else None,
                            servings=r["servings"], recipeServings=r["recipeServings"], lines=lines, expected=expected,
                            status="known" if r["id"] in design_known else "new",
                            source="staging:" + ("hand label" if hand else "derived label") + (", " + GOLDEN_GATE if r["origin"] == "cited" else ""),
                            note=design_known.get(r["id"], "")))
    (out / "recipes.json").write_text(json.dumps(recipes, ensure_ascii=False, indent=1) + "\n", encoding="utf-8")
    print(f"recipes.json: {len(recipes)} recipes", file=sys.stderr)


def main():
    p = argparse.ArgumentParser(description="Builds the golden-set fixture files from captured staging rows and the judged evidence.")
    p.add_argument("--evidence", required=True, type=Path)
    p.add_argument("--captures", required=True, type=Path)
    p.add_argument("--out", required=True, type=Path)
    a = p.parse_args()
    a.out.mkdir(parents=True, exist_ok=True)
    for name, target in (("catalog.tsv", "catalog.tsv"), ("usda.tsv", "usda-catalog.tsv"), ("measurements.tsv", "measurements.tsv"), ("line-labels.tsv", "line-labels.tsv")):
        rows = read_tsv(a.captures / name)
        write_tsv(a.out / target, list(rows[0].keys()), rows)
    build_lines(a.out)
    build_wordings(a.out)
    build_line_kinds(a.out)
    build_veto(a.out)
    build_link_pairs(a.out, a.evidence, a.captures)
    build_links(a.out, a.evidence, a.captures)
    vocab_lines = []
    build_compound(a.out, a.evidence, a.captures, vocab_lines)
    build_vocabulary(a.out, a.captures, vocab_lines)
    build_unfold(a.out, a.captures)
    build_recipes(a.out, a.captures)
    build_prose(a.out, a.captures)


if __name__ == "__main__":
    main()

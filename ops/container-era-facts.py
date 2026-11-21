#!/usr/bin/env python3
"""Mines the sized container lines of old and undated books into the container-era facts.

Reads a capture of the recipe corpus (ops/golden/container-era-lines.sql through
ops/golden/capture.sh, read-only, or the constructed fixture
nom-api/Nom.Api.Tests/Fixtures/Golden/container-era-lines.tsv): one row per sized line in a
weight or volume unit, with its book, era, USDA food, raw text, the quantity and unit the line
is stored in, and the food's density (a volume counts only with one). It never reads a network
or a database itself.

For every food, container kind and size word it writes, with counts:
  book  the size the sized lines of one book give (one size, within 5 %, holds more than half of
        the book's lines and at least twice the next size);
  era   the size the books of one era give (each book counted once per size it states; at least
        two books state the mode, it holds at least half of the votes and twice the next size).
Refused sizes are written too, with the reason. The rule is written apart from the C# code
(ContainerCorpusSizes): the grams of one container come here from the stored quantity and the
line's leading count, there from NOM's line parser, so the two readings of one capture can be
compared row by row (Nom.Import --container-sizes --corpus --facts writes the same columns).

usage: container-era-facts.py <lines.tsv> > container-era-facts.tsv
"""
import argparse
import collections
import re
import sys
from fractions import Fraction

OZ = 28.349523125
GRAMS = {"ounce": OZ, "pound": 453.59237, "gram": 1.0, "kilogram": 1000.0}
MILLILITERS = {"milliliter": 1.0, "liter": 1000.0, "cup": 236.5882365, "tablespoon": 14.78676478, "teaspoon": 4.928921594}
KINDS = [
    (r"cans?|tins?", "can"),
    (r"jars?", "jar"),
    (r"bottles?", "bottle"),
    (r"box(es)?", "box"),
    (r"bags?", "bag"),
    (r"packets?|pouch(es)?|sachets?", "packet"),
    (r"envelopes?", "envelope"),
    (r"cartons?", "carton"),
    (r"tubs?|containers?", "tub"),
    (r"packages?|pkgs?|packs?", "package"),
]
SIZE_BEFORE = re.compile(
    r"\b(?P<size>small|large|big|large-size|small-size)\s+(?:size\s+)?(?:cans?|tins?|jars?|bottles?|box(?:es)?|bags?|packets?|"
    r"pouch(?:es)?|envelopes?|cartons?|tubs?|containers?|packages?|pkgs?|packs?)\b", re.I)
NUMBERED = re.compile(r"(?:\bno\.?\s*|#\s*)\d", re.I)
UNICODE = {"½": Fraction(1, 2), "¼": Fraction(1, 4), "¾": Fraction(3, 4), "⅓": Fraction(1, 3), "⅔": Fraction(2, 3), "⅛": Fraction(1, 8)}
LEADING = re.compile(r"^\s*(?:(?P<whole>\d+)[\s-]+(?P<num>\d+)/(?P<den>\d+)|(?P<frac>\d+)/(?P<fden>\d+)|(?P<int>\d+(?:\.\d+)?)?(?P<uni>[½¼¾⅓⅔⅛])?)")
WORDS = {"one": 1, "two": 2, "three": 3, "four": 4, "five": 5, "six": 6, "a": 1, "an": 1}
BAND = 0.05
ERAS = ("historic", "unknown")
COUNT = r"(?:\d+(?:[\s-]+\d+/\d+|/\d+|\.\d+)?[½¼¾⅓⅔⅛]?|[½¼¾⅓⅔⅛]|one|two|three|four|five|six|a|an)"
SIZE = r"(?:about\s+)?(?:\d+(?:[\s-]+\d+/\d+|/\d+|\.\d+)?[½¼¾⅓⅔⅛]?|[½¼¾⅓⅔⅛])[\s-]*(?:fl\.?\s*)?(?:oz|ounces?|lbs?|pounds?|g|grams?|kg|ml|pints?|quarts?|cups?)\.?"
QUALIFIER = r"(?:(?:large|small|medium|big|regular|standard|whole|full|tall|jumbo)\s+)*"
CONTAINER = r"(?:cans?|tins?|jars?|bottles?|box(?:es)?|bags?|packets?|pouch(?:es)?|sachets?|envelopes?|cartons?|tubs?|containers?|packages?|pkgs?|packs?)\b\.?"
SIZE_FIRST = re.compile(r"^\s*" + SIZE + r"\s*" + QUALIFIER + CONTAINER, re.I)
STATED = [
    SIZE_FIRST,
    re.compile(r"^\s*" + COUNT + r"\s*\(?\s*" + SIZE + r"\s*\)?\s*" + QUALIFIER + CONTAINER, re.I),
    re.compile(r"^\s*" + COUNT + r"\s+" + QUALIFIER + CONTAINER + r"\s*[(,]?\s*" + SIZE, re.I),
    re.compile(r"^\s*" + COUNT + r"\s+" + QUALIFIER + CONTAINER + r"[^()]*\(\s*" + SIZE, re.I),
]


def states_size(line):
    """Whether the line states the size of its container by the count and the container word, as "1 (3 oz) package", "1 package (3 ounces)" or "10-oz. can"."""
    return any(p.search(line) for p in STATED)


def kind_of(line):
    for word in re.findall(r"[A-Za-z]+", line):
        for pattern, kind in KINDS:
            if re.fullmatch(pattern, word, re.I):
                return kind
    return None


def size_word(line):
    m = SIZE_BEFORE.search(line)
    if not m:
        return ""
    return "small" if m.group("size").lower().startswith("small") else "large"


def count_of(line):
    if SIZE_FIRST.search(line):
        return 1.0
    m = LEADING.match(line)
    if m and m.group("whole"):
        return float(int(m.group("whole")) + Fraction(int(m.group("num")), int(m.group("den"))))
    if m and m.group("frac"):
        return float(Fraction(int(m.group("frac")), int(m.group("fden"))))
    if m and (m.group("int") or m.group("uni")):
        return float(Fraction(m.group("int") or "0") + UNICODE.get(m.group("uni") or "", Fraction(0)))
    first = line.strip().split(" ", 1)[0].lower()
    return float(WORDS.get(first, 1))


def grams_per_container(row):
    unit = row["unit"].strip().lower()
    try:
        quantity = float(row["quantity"])
    except ValueError:
        return None
    if unit in GRAMS:
        per = GRAMS[unit]
    elif unit in MILLILITERS and row.get("density", "-") not in ("", "-"):
        per = MILLILITERS[unit] * float(row["density"])
    else:
        return None
    count = count_of(row["line"])
    if quantity <= 0 or count <= 0 or per <= 0:
        return None
    return round(quantity * per / count, 1)


def read_lines(path):
    lines = []
    with open(path, encoding="utf-8") as f:
        header = None
        for raw in f:
            raw = raw.rstrip("\n")
            if not raw or raw.startswith("#"):
                continue
            cells = raw.split("\t")
            if header is None:
                header = cells
                continue
            row = dict(zip(header, cells))
            if row["book"] in ("", "-") or row["era"] not in ERAS:
                continue
            kind = kind_of(row["line"])
            if kind is None or NUMBERED.search(row["line"]) or not states_size(row["line"]):
                continue
            grams = grams_per_container(row)
            if grams is None or grams <= 0:
                continue
            lines.append({"book": row["book"], "era": row["era"], "food": row["fdcId"], "kind": kind,
                          "word": size_word(row["line"]), "grams": grams, "line": row["line"]})
    return lines


def gram(value):
    return int(round(value))


def within(size, mode):
    return abs(size - mode) <= BAND * mode


def mode_of(sizes):
    return sorted(sizes.items(), key=lambda kv: (-kv[1], kv[0]))[0][0]


def decide(votes, minimum, few):
    total = sum(votes.values())
    mode = mode_of(votes)
    band = sum(n for s, n in votes.items() if within(s, mode))
    rest = {s: n for s, n in votes.items() if not within(s, mode)}
    runner = 0
    if rest:
        second = mode_of(rest)
        runner = sum(n for s, n in rest.items() if within(s, second))
    if total < minimum:
        refusal = few
    elif band < 0.5 * total:
        refusal = "no clear mode"
    elif band < 2 * runner:
        refusal = "runner-up too close"
    else:
        refusal = None
    return mode, band, total, runner, refusal


def number(value):
    text = f"{value:.1f}"
    return text[:-2] if text.endswith(".0") else text


def facts(lines):
    rows = []
    by_book = collections.defaultdict(list)
    by_era = collections.defaultdict(list)
    for line in lines:
        by_book[(line["book"], food_key(line["food"]), line["kind"], line["word"])].append(line)
        by_era[(line["era"], food_key(line["food"]), line["kind"], line["word"])].append(line)
    for (book, food, kind, word), group in sorted(by_book.items()):
        votes = collections.Counter(gram(l["grams"]) for l in group)
        rows.append(row("book", book, food, kind, word, group, votes, decide(votes, 1, "few products")))
    for (era, food, kind, word), group in sorted(by_era.items()):
        votes = collections.Counter(g for _, g in {(l["book"], gram(l["grams"])) for l in group})
        rows.append(row("era", era, food, kind, word, group, votes, decide(votes, 2, "fewer than two books")))
    return rows


def food_key(food):
    return (0, int(food), "") if food.isdigit() else (1, 0, food)


def row(tier, scope, food, kind, word, group, votes, decided):
    food = food[1] if food[0] == 0 else food[2]
    mode, band, total, runner, refusal = decided
    sizes = ";".join(f"{g}:{n}" for g, n in sorted(votes.items()))
    return [tier, scope, str(food), kind, word or "-", str(mode), str(len(group)), str(len({l["book"] for l in group})),
            str(band), str(total), str(runner), number(min(l["grams"] for l in group)), number(max(l["grams"] for l in group)),
            sizes, refusal or "counts"]


def main(argv=None):
    p = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    p.add_argument("lines")
    a = p.parse_args(argv)
    out = ["id\ttier\tscope\tfood\tkind\tsizeWord\tgrams\tlines\tbooks\tband\tvotes\trunnerUp\tmin\tmax\tsizes\tverdict"]
    for n, cells in enumerate(facts(read_lines(a.lines)), 1):
        out.append(f"f-{n:03d}\t" + "\t".join(cells))
    sys.stdout.write("\n".join(out) + "\n")


if __name__ == "__main__":
    main()

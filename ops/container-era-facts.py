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
        two different books state a size within the mode's band, it holds at least half of the
        votes and twice the next size).
Refused sizes are written too, with the reason. The rule is written apart from the C# code
(ContainerCorpusSizes): the grams of one container come here from the stored quantity and the
line's leading count, there from NOM's line parser, so the two readings of one capture can be
compared row by row (Nom.Import --container-sizes --corpus --facts writes the same columns).

A line is not read when its text does not state one size of one container: a range or an
alternative of sizes ("7½ to 8 oz.", "8-oz. or 1-lb."), a range of containers ("one or two 2-oz.
cans"), a count followed by "half" ("1 half pint can": a half-pint can or one and a half), a size
"and a half" of its unit, a size written with super- or subscript digits ("13³⁄₄ oz."), or a
container used as a vessel ("1 qt. jar with fresh mint leaves"). A line that names a flavoured
product ("strawberry gelatin", "raspberry-flavored gelatin") counts only on a food whose
description names a flavour or a dessert, so flavoured gelatin packages never size plain gelatine.
A recipe with a source URL outside the book archives (a web page, USDA MyPlate Kitchen) is modern
whatever its attribution, unless its era row says otherwise, and never enters the old-book tiers.

usage: container-era-facts.py <lines.tsv> > container-era-facts.tsv
       container-era-facts.py --lines <lines.tsv> > container-era-readings.tsv
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
SIZE_UNIT = r"(?:fl\.?\s*)?(?:oz|ounces?|lbs?|pounds?|g|grams?|kg|ml|pints?|pts?|quarts?|qts?|cups?)\b"
NOT_ONE_SIZE = [
    re.compile(r"(?:\d+(?:[./]\d+)?|[½¼¾⅓⅔⅛])\s*(?:-|–|to|or)\s*\d+(?:[./]\d+)?\s*-?\s*" + SIZE_UNIT, re.I),
    re.compile(r"\b(?:oz|ounces?|lbs?|pounds?|g|grams?|kg|ml|pints?|pts?|quarts?|qts?|cups?)\.?\s+(?:or|to)\s+(?:\d|one\b|two\b|a\b)", re.I),
    re.compile(r"^\s*(?:\d+|one|two|three|four|a)\s+(?:or|to)\s+(?:\d+|one|two|three|four|five|six)\b", re.I),
    re.compile(r"\d\s+half\b", re.I),
    re.compile(r"\b(?:pints?|quarts?|pounds?|cups?|ounces?|gallons?)\s+and\s+a\s+half\b", re.I),
    re.compile(r"[⁰¹²³⁴⁵⁶⁷⁸⁹₀₁₂₃₄₅₆₇₈₉⁄]"),
    re.compile(r"\b(?:cans?|tins?|jars?|bottles?|box(?:es)?|bags?|packets?|pouch(?:es)?|sachets?|envelopes?|cartons?|tubs?|containers?|packages?|pkgs?|packs?)\.?\s+with\b", re.I),
]
FLAVOURED = re.compile(r"\b(?:[a-z]+-)?flavou?red\b|\b(?:strawberry|raspberry|cherry|lime|lemon|orange|grape|peach|apricot|blackberry|pineapple|fruit)\s+(?:gelatine?|jell-?o)\b", re.I)
FLAVOUR_FOOD = re.compile(r"flavo|dessert", re.I)
BOOK_ARCHIVES = ("gutenberg.org", "archive.org", "loc.gov", "hathitrust.org")
UNICODE = {"½": Fraction(1, 2), "¼": Fraction(1, 4), "¾": Fraction(3, 4), "⅓": Fraction(1, 3), "⅔": Fraction(2, 3), "⅛": Fraction(1, 8)}
LEADING = re.compile(r"^\s*(?:(?P<whole>\d+)[\s-]+(?P<num>\d+)/(?P<den>\d+)|(?P<frac>\d+)/(?P<fden>\d+)|(?P<int>\d+(?:\.\d+)?)?(?P<uni>[½¼¾⅓⅔⅛])?)")
WORDS = {"one": 1, "two": 2, "three": 3, "four": 4, "five": 5, "six": 6, "a": 1, "an": 1}
BAND = 0.05
ERAS = ("historic", "unknown")
COUNT = r"(?:\d+(?:[\s-]+\d+/\d+|/\d+|\.\d+)?[½¼¾⅓⅔⅛]?|[½¼¾⅓⅔⅛]|one|two|three|four|five|six|a|an)"
SIZE = r"(?:about\s+)?(?:\d+(?:[\s-]+\d+/\d+|/\d+|\.\d+)?[½¼¾⅓⅔⅛]?|[½¼¾⅓⅔⅛])[\s-]*(?:fl\.?\s*)?(?:oz|ounces?|lbs?|pounds?|g|grams?|kg|ml|pints?|pts?|quarts?|qts?|cups?)\.?"
QUALIFIER = r"(?:(?:large|small|medium|big|regular|standard|whole|full|tall|jumbo)\s+)*"
CONTAINER = r"(?:cans?|tins?|jars?|bottles?|box(?:es)?|bags?|packets?|pouch(?:es)?|sachets?|envelopes?|cartons?|tubs?|containers?|packages?|pkgs?|packs?)\b\.?"
ONE_CONTAINER = r"(?:can|tin|jar|bottle|box|bag|packet|pouch|sachet|envelope|carton|tub|container|package|pkg|pack)\b\.?"
UNIT = r"(?:fl\.?\s*)?(?:oz|ounces?|lbs?|pounds?|g|grams?|kg|ml|pints?|pts?|quarts?|qts?|cups?)\.?"
POUNDS_AND_OUNCES = r"\d+\s*(?:lbs?|pounds?)\.?[\s,]*(?:and\s+)?\d+(?:\.\d+)?\s*(?:oz|ounces?)\.?"
SIZE_WORD = (r"(?:three[\s-]quarters|two[\s-]thirds|one[\s-]third|a\s+half|one[\s-]half|a\s+quarter|one[\s-]quarter|half|quarter|"
             r"one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|an?)")
LEAD_SIZE = r"(?:about\s+)?(?:" + POUNDS_AND_OUNCES + r"|" + SIZE_WORD + r"(?:\s+and\s+" + SIZE_WORD + r")?[\s-]+" + UNIT + r")"
SIZE_FIRST = re.compile(r"^\s*(?:" + LEAD_SIZE + r"|" + SIZE + r")\s*" + QUALIFIER + ONE_CONTAINER, re.I)
WORD_SIZE = SIZE_WORD + r"[\s-]+" + UNIT
PART_OF = r"\(\s*(?:a\s+|one\s+)?(?:quarter|half|third)\s+of\s+an?\s+" + UNIT + r"\s*\)"
STATED = [
    SIZE_FIRST,
    re.compile(r"^\s*" + COUNT + r"\s+\(?\s*" + WORD_SIZE + r"\s*\)?\s*" + QUALIFIER + CONTAINER, re.I),
    re.compile(r"^\s*" + COUNT + r"\s+" + QUALIFIER + PART_OF + r"\s*" + CONTAINER, re.I),
    re.compile(r"^\s*" + COUNT + r"\s*\(?\s*" + SIZE + r"\s*\)?\s*" + QUALIFIER + CONTAINER, re.I),
    re.compile(r"^\s*" + COUNT + r"\s+" + QUALIFIER + CONTAINER + r"\s*[(,]?\s*" + SIZE, re.I),
    re.compile(r"^\s*" + COUNT + r"\s+" + QUALIFIER + CONTAINER + r"[^()]*\(\s*" + SIZE, re.I),
]


def states_size(line):
    """Whether the line states the size of its container by the count and the container word, as "1 (3 oz) package", "1 package (3 ounces)" or "10-oz. can", or by a size before one container with no count ("1 lb. 13 oz. can", "one pound can"); a size before plural containers with no count ("2 lb. cans") says not how many and states none."""
    return any(p.search(line) for p in STATED)


def one_size(line):
    """Whether the line states one size of one container (see the module docstring)."""
    return not any(p.search(line) for p in NOT_ONE_SIZE)


def names_its_food(line, food):
    """A line naming a flavoured product counts only on a food whose description names a flavour or a dessert."""
    return not FLAVOURED.search(line) or bool(FLAVOUR_FOOD.search(food or ""))


def era_of(row):
    """The era a capture row reads in: its era row's, except that a recipe from a web page outside the book archives is modern."""
    era = row["era"]
    host = (row.get("host") or "-").strip().lower()
    if era == "unknown" and host not in ("", "-") and not any(host == h or host.endswith("." + h) for h in BOOK_ARCHIVES):
        return "modern"
    return era


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


def read_lines(path, readings=None):
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
            era = era_of(row)
            if row["book"] in ("", "-") or era not in ERAS:
                continue
            kind = kind_of(row["line"])
            if kind is None or NUMBERED.search(row["line"]) or not states_size(row["line"]) or not one_size(row["line"]):
                continue
            if not names_its_food(row["line"], row.get("food")):
                continue
            grams = grams_per_container(row)
            if grams is None or grams <= 0:
                continue
            lines.append({"book": row["book"], "era": era, "food": row["fdcId"], "kind": kind,
                          "word": size_word(row["line"]), "grams": grams, "line": row["line"]})
            if readings is not None:
                readings.append(row["id"])
    return lines


def gram(value):
    return int(round(value))


def within(size, mode):
    return abs(size - mode) <= BAND * mode


def mode_of(sizes):
    return sorted(sizes.items(), key=lambda kv: (-kv[1], kv[0]))[0][0]


def decide(votes, minimum, few, more_than_half=False, books=None):
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
    elif more_than_half and 2 * band <= total:
        refusal = "no clear mode"
    elif books is not None and len({b for b, g in books if within(g, mode)}) < minimum:
        refusal = few
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
        rows.append(row("book", book, food, kind, word, group, votes, decide(votes, 1, "few products", more_than_half=True)))
    for (era, food, kind, word), group in sorted(by_era.items()):
        stated = {(l["book"], gram(l["grams"])) for l in group}
        votes = collections.Counter(g for _, g in stated)
        rows.append(row("era", era, food, kind, word, group, votes, decide(votes, 2, "fewer than two books", books=stated)))
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
    p.add_argument("--lines", dest="per_line", action="store_true", help="write each line read with its kind, size word and grams instead of the facts")
    a = p.parse_args(argv)
    if a.per_line:
        ids = []
        read = read_lines(a.lines, ids)
        rows = ["id\tera\tkind\tsizeWord\tgrams"] + [f"{i}\t{l['era']}\t{l['kind']}\t{l['word'] or '-'}\t{number(l['grams'])}" for i, l in zip(ids, read)]
        sys.stdout.write("\n".join(rows) + "\n")
        return
    out = ["id\ttier\tscope\tfood\tkind\tsizeWord\tgrams\tlines\tbooks\tband\tvotes\trunnerUp\tmin\tmax\tsizes\tverdict"]
    for n, cells in enumerate(facts(read_lines(a.lines)), 1):
        out.append(f"f-{n:03d}\t" + "\t".join(cells))
    sys.stdout.write("\n".join(out) + "\n")


if __name__ == "__main__":
    main()

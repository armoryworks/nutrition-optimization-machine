#!/usr/bin/env python3
import argparse
import re
import sys
from pathlib import Path

SOURCE = "rule:construction"
SEP = " ¦ "
PER_RECIPE = 13

STOVE = ["low", "medium-low", "medium", "medium-high", "high"]
OVEN = ["slow", "moderate", "moderately hot", "hot", "very hot"]

CUES = [
    ("golden", ["dark brown", "just set"]),
    ("smells good", ["browns", "turns dark"]),
    ("a little thicker", ["very thick", "it boils"]),
    ("tender", ["still firm", "falling apart"]),
    ("bright green", ["olive green", "very soft"]),
    ("smooth", ["thick and lumpy", "frothy"]),
    ("until stiff", ["until foamy", "until just frothy"]),
    ("soft and light", ["melted", "warm"]),
    ("baking it through", ["browning only the top", "warming it only"]),
    ("stop cooking", ["soak up water", "keep warm"]),
]

ACTIONS = [
    ("simmer", "boil hard"),
    ("Bake", "Steam"),
    ("bake", "steam"),
    ("fry", "boil"),
    ("Boil", "Steam"),
    ("Melt", "Brown"),
    ("Rinse", "Peel"),
    ("Wash", "Toast"),
    ("Grease", "Flour"),
    ("Whip", "Fold"),
    ("soak", "cook"),
    ("Drain", "Rinse"),
    ("Stew", "Fry"),
    ("Sift", "Knead"),
    ("Pack", "Mash"),
    ("whisk", "melt"),
    ("Whisk", "Fold"),
    ("sizzle", "brown"),
]

INGREDIENTS = [
    ("garlic", "onion"),
    ("cream", "milk"),
    ("rice", "barley"),
    ("raisins", "currants"),
    ("almonds", "walnuts"),
    ("peach", "pear"),
    ("celery", "leeks"),
    ("nuts", "seeds"),
    ("oats", "bran"),
    ("lemon juice", "vinegar"),
    ("egg yolks", "egg whites"),
]

ADDED = [
    ("added", "Cover it with a lid and leave it to stand."),
    ("added", "Strain it through a fine sieve."),
    ("added", "Season it with black pepper."),
    ("added", "Brush the top with beaten egg."),
    ("added", "Scatter chopped parsley over it."),
]

UNITS = [
    ("tablespoon", "teaspoon"),
    ("teaspoon", "tablespoon"),
    ("seconds", "minutes"),
    ("cups", "pints"),
    ("cup ", "pint "),
    ("inches", "centimetres"),
    ("oz", "lb"),
]

KIND_CAPS = {"heat": 2, "cue": 2, "dropped": 2, "added": 2, "action": 2, "order": 1, "ingredient": 1, "unit": 1, "time": 1, "range": 1}
KIND_ORDER = ["heat", "cue", "action", "dropped", "added", "order", "ingredient", "time", "range", "unit"]


def read_rows(path):
    lines = path.read_text(encoding="utf-8").split("\n")
    header = lines[0].split("\t")
    rows = [dict(zip(header, line.split("\t"))) for line in lines[1:] if line and not line.startswith("#")]
    return header, rows


def word(text, pattern):
    return re.search(r"(?<![\w-])" + re.escape(pattern) + r"(?![\w-])", text)


def replace_once(text, old, new):
    m = word(text, old)
    return None if m is None else text[: m.start()] + new + text[m.end():]


def heat_edits(steps, offset):
    for i, step in enumerate(steps):
        for m in re.finditer(r"\b(on|over)\s+(medium-low|medium-high|medium|low|high)\b", step):
            level = STOVE.index(m.group(2))
            for shift in (2, -2):
                other = STOVE[(level + shift) % len(STOVE)]
                yield i, step[: m.start(2)] + other + step[m.end(2):], f"the heat level changed: {m.group(2)} became {other}"
    for i, step in enumerate(steps):
        if "oven" not in step:
            continue
        for m in re.finditer(r"\b(very hot|moderately hot|moderate|slow|hot)\b", step):
            level = OVEN.index(m.group(1))
            for shift in (2, -2):
                other = OVEN[(level + shift) % len(OVEN)]
                yield i, step[: m.start(1)] + other + step[m.end(1):], f"the oven changed: {m.group(1)} became {other}"
    for i, step in enumerate(steps):
        m = re.search(r"\b(Serve|serve) hot\b", step)
        if m:
            yield i, step[: m.start()] + m.group(1) + " cold" + step[m.end():], "the serving changed: hot became cold"


def cue_edits(steps, offset):
    for old, news in CUES:
        for i, step in enumerate(steps):
            if word(step, old):
                for new in news:
                    yield i, replace_once(step, old, new), f"the doneness cue changed: {old} became {new}"
                break
    for i, step in enumerate(steps):
        m = re.search(r",\s*(?:just\s+)?until [^.;,]*", step)
        if m:
            yield i, step[: m.start()] + step[m.end():], f"the doneness cue left out: {m.group(0).strip(', ')}"


def action_edits(steps, offset):
    for old, new in ACTIONS:
        for i, step in enumerate(steps):
            if word(step, old):
                yield i, replace_once(step, old, new), f"the action changed: {old} became {new}"
                break


def dropped_edits(steps, offset):
    for i, step in enumerate(steps):
        sentences = re.split(r"(?<=\.)\s+", step)
        if len(sentences) >= 2:
            for k in range(len(sentences)):
                rest = " ".join(s for j, s in enumerate(sentences) if j != k)
                yield i, rest, f"a preparation left out: {sentences[k].rstrip('.')}"
        for sep in (", then ", "; "):
            if sep in step:
                head, tail = step.split(sep, 1)
                yield i, tail[0].upper() + tail[1:], f"a preparation left out: {head}"
                yield i, head.rstrip(".") + ".", f"a preparation left out: {tail.rstrip('.')}"


def added_edits(steps, offset):
    for n in range(len(ADDED)):
        sentence = ADDED[(n + offset) % len(ADDED)][1]
        i = (n * 2 + 1 + offset) % len(steps)
        yield i, steps[i].rstrip() + " " + sentence, f"an action the source lacks: {sentence.rstrip('.')}"


def order_edits(steps, offset):
    for k in range(len(steps) - 1):
        i = (k + offset) % (len(steps) - 1)
        if steps[i] != steps[i + 1]:
            yield (i, i + 1), None, f"two steps swapped: step {i + 1} and step {i + 2}"


def ingredient_edits(steps, offset):
    for old, new in INGREDIENTS:
        for i, step in enumerate(steps):
            if word(step, old):
                yield i, replace_once(step, old, new), f"an ingredient changed: {old} became {new}"
                break


def time_edits(steps, offset):
    for i, step in enumerate(steps):
        for m in re.finditer(r"\b(\d+) (minutes|seconds)\b", step):
            n = int(m.group(1))
            other = n + 15 if n >= 10 else n * 3
            yield i, step[: m.start(1)] + str(other) + step[m.end(1):], f"a time changed: {n} {m.group(2)} became {other} {m.group(2)}"


def range_edits(steps, offset):
    for i, step in enumerate(steps):
        for m in re.finditer(r"\b(?:from )?(\d+|[\d/]+ of an hour) (to|or) (\d+)\b", step):
            yield i, step[: m.start()] + m.group(3) + step[m.end():], f"a range collapsed: {m.group(0)} became {m.group(3)}"
            yield i, step[: m.start()] + m.group(1) + step[m.end():], f"a range collapsed: {m.group(0)} became {m.group(1)}"


def unit_edits(steps, offset):
    for old, new in UNITS:
        for i, step in enumerate(steps):
            if old.endswith(" ") and old in step:
                yield i, step.replace(old, new, 1), f"a unit swapped: {old.strip()} became {new.strip()}"
                break
            if not old.endswith(" ") and word(step, old):
                yield i, replace_once(step, old, new), f"a unit swapped: {old} became {new}"
                break


EDITS = {
    "heat": heat_edits,
    "cue": cue_edits,
    "action": action_edits,
    "dropped": dropped_edits,
    "added": added_edits,
    "order": order_edits,
    "ingredient": ingredient_edits,
    "time": time_edits,
    "range": range_edits,
    "unit": unit_edits,
}


def planted(faithful, taken, offset):
    steps = faithful["rewrite"].split(SEP)
    out = []
    counts = {}
    for kind in KIND_ORDER:
        for where, text, what in EDITS[kind](steps, offset):
            if counts.get(kind, 0) >= KIND_CAPS[kind] or len(out) >= PER_RECIPE:
                break
            new = list(steps)
            if isinstance(where, tuple):
                a, b = where
                new[a], new[b] = new[b], new[a]
            else:
                if not text or text == steps[where]:
                    continue
                new[where] = text
            rewrite = SEP.join(new)
            if rewrite in taken:
                continue
            taken.add(rewrite)
            counts[kind] = counts.get(kind, 0) + 1
            out.append((kind, rewrite, what))
    return out


def main():
    parser = argparse.ArgumentParser(description="Plant rule-given faults into the faithful rewrites of prose-judge.tsv; new rows are added as new, existing rows are never changed.")
    parser.add_argument("--golden", default=str(Path(__file__).resolve().parents[2] / "nom-api/Nom.Api.Tests/Fixtures/Golden"))
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args()
    path = Path(args.golden) / "prose-judge.tsv"
    header, rows = read_rows(path)
    ids = {r["id"] for r in rows}
    taken = {r["rewrite"] for r in rows}
    result = []
    added = 0
    for offset, recipe in enumerate(dict.fromkeys(r["recipe"] for r in rows)):
        own = [r for r in rows if r["recipe"] == recipe]
        result.extend(own)
        faithful = next(r for r in own if r["expect"] == "faithful")
        numbers = {}
        for kind, rewrite, what in planted(faithful, taken, offset):
            numbers[kind] = numbers.get(kind, 0) + 1
            row_id = f"{recipe}-pl-{kind}-{numbers[kind]}"
            if row_id in ids:
                continue
            result.append({
                "id": row_id, "recipe": recipe, "title": faithful["title"], "lines": faithful["lines"], "method": faithful["method"],
                "rewrite": rewrite, "rewriteDescription": faithful["rewriteDescription"], "expect": "fault", "fault": kind, "check2": "",
                "source": SOURCE, "status": "new", "note": f"{faithful['id']} with one edit planted by ops/golden/plant-prose-faults.py: {what}",
            })
            added += 1
    print(f"prose-judge.tsv: {len(rows)} rows, {added} planted rows added", file=sys.stderr)
    if args.dry_run:
        for r in (r for r in result if r["id"] not in ids):
            print(r["id"], "|", r["note"], file=sys.stderr)
        return
    with open(path, "w", encoding="utf-8", newline="") as f:
        f.write("\t".join(header) + "\n")
        for r in result:
            f.write("\t".join(r.get(h, "") for h in header) + "\n")


if __name__ == "__main__":
    main()

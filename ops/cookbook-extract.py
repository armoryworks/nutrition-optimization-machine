#!/usr/bin/env python3
"""
Extract the recipes of a public-domain cookbook text again, for the recipe rebuild (reprocess.md
2e, plan item B11). Run on the harvest host, where the book texts are (pg*.txt). The records it
writes are attached to the recipes already imported from the book as a newer source version
(ops/recipe-source-attach.py --new-version), and the reprocess lane then rebuilds those recipes'
lines and steps from them.

RULES
-----
1. Nothing is fetched. The script reads a local book text; its one network peer is the model host
   the operator names (Ollama /api/chat), which only reads the text it is sent.
2. Chunks are cut at recipe boundaries (the title lines the text shows), never inside a recipe, so
   a recipe is never cut at a chunk edge as the old 4,000-character chunks cut it (the "only one
   ingredient line" and "only one step" records). A single recipe longer than --hard-limit is cut
   at paragraph ends and its records are marked "split".
3. Fidelity check, rule-given, on every record (no model): every ingredient line must appear in the
   chunk's text after normalization (entities, spacing, case, quotes); otherwise every number in it
   must appear in the chunk and every word of it be found there. A record that fails is dropped and
   written to --rejects with its reason, never to --out.
4. Translation (--translate, for Artusi's "La scienza in cucina"): the model writes English and
   quotes, for each ingredient line and step, the Italian line it translates. The Italian line must
   appear in the chunk, every number and unit of the English line must equal one in the Italian
   line (grammi is grams, chilogrammi kilograms, litro a litre, cucchiaio a tablespoon and so on),
   none may be added or dropped, and each English ingredient line must come from an Italian line
   of its own. The model supplies no amount: every amount already stands in the source. A record
   that fails is dropped and logged.
5. Each record may name the recipe whose source it replaces ("replacesRecipeId"), from the log of
   the first attach of the book (--recipe-map, matched by the record's name or the title it quotes)
   or from a CSV of name to recipe id (--pin-csv). The attach script sends it as a pin.

USAGE
-----
    ./ops/cookbook-extract.py ~/cookbook-work/pg59047.txt --host http://<model host>:11434 \\
        --model qwen2.5:14b-instruct --translate --out cookbook-artusi-v2.jsonl --rejects artusi-rejects.jsonl \\
        --recipe-map attach-apply.jsonl --old-file cookbook-pg59047.jsonl
    ./ops/recipe-source-attach.py cookbook-artusi-v2.jsonl --attribution-csv books.csv --new-version [--apply]
"""

from __future__ import annotations

import argparse
import csv
import html
import json
import re
import sys
import unicodedata
import urllib.request
from collections import Counter
from dataclasses import dataclass, field
from fractions import Fraction
from typing import Callable, Iterable

DEFAULT_LIMIT = 4000
HARD_LIMIT = 12000
USER_AGENT = "nom-cookbook-extract/1.0"
MATCHED = ("matched", "already-attached")

Ask = Callable[[str, str], str]


def log(message: str) -> None:
    print(message, file=sys.stderr, flush=True)


INGREDIENT_START = re.compile(r"^\s*(?:[\d½¼¾⅓⅔⅛]|(?:one|two|three|four|five|six|a|an|half)\s+(?:cup|pint|quart|pound|ounce|tablespoon|teaspoon|gill)s?\b)", re.I)
NUMBERED_TITLE = re.compile(r"^\s*\d{1,4}\.\s+\S")
SENTENCE_END = re.compile(r"[.!?;]\s+\S")


def is_title(lines: list[str], i: int) -> bool:
    """A recipe title: a short line after a blank line (or the text's start), before a non-blank line,
    with no sentence end inside, not a line of an ingredient list, and either numbered ("123. Risotto"),
    in capitals, or with most of its words capitalized."""
    line = lines[i].strip()
    if not line or len(line) > 80 or line.endswith(","):
        return False
    if i > 0 and lines[i - 1].strip():
        return False
    if i + 1 >= len(lines) or not lines[i + 1].strip():
        if not (i + 2 < len(lines) and lines[i + 2].strip()):
            return False
    if NUMBERED_TITLE.match(line):
        return not SENTENCE_END.search(line.split(".", 1)[1])
    if SENTENCE_END.search(line) or INGREDIENT_START.match(line):
        return False
    letters = [c for c in line if c.isalpha()]
    if letters and all(c.isupper() for c in letters):
        return True
    words = [w for w in re.findall(r"[^\W\d_]+", line) if len(w) > 3]
    return bool(words) and sum(1 for w in words if w[0].isupper()) / len(words) >= 0.6


def title_offsets(text: str) -> list[int]:
    """The character offset of every title line."""
    lines = text.split("\n")
    offsets, at = [], 0
    for i, line in enumerate(lines):
        if is_title(lines, i):
            offsets.append(at)
        at += len(line) + 1
    return offsets


@dataclass
class Chunk:
    start: int
    end: int
    text: str
    split: bool = False


def chunks(text: str, limit: int = DEFAULT_LIMIT, hard: int = HARD_LIMIT) -> list[Chunk]:
    """The text cut into chunks that begin at title lines and hold whole recipes, at most `limit`
    characters each unless one recipe is longer; a recipe longer than `hard` is cut at paragraph ends."""
    starts = sorted(set([0] + title_offsets(text)))
    blocks = [(s, e) for s, e in zip(starts, starts[1:] + [len(text)]) if text[s:e].strip()]
    out: list[Chunk] = []
    current: tuple[int, int] | None = None
    for start, end in blocks:
        if end - start > hard:
            if current:
                out.append(Chunk(current[0], current[1], text[current[0]:current[1]]))
                current = None
            out.extend(_split(text, start, end, hard))
            continue
        if current and end - current[0] <= limit:
            current = (current[0], end)
            continue
        if current:
            out.append(Chunk(current[0], current[1], text[current[0]:current[1]]))
        current = (start, end)
    if current:
        out.append(Chunk(current[0], current[1], text[current[0]:current[1]]))
    return out


def _split(text: str, start: int, end: int, hard: int) -> list[Chunk]:
    """One over-long recipe cut at paragraph ends into pieces of at most `hard` characters where the paragraphs allow."""
    ends = [start + m.end() for m in re.finditer(r"\n\s*\n", text[start:end])] + [end]
    bounds, at, last = [], start, start
    for cut in ends:
        if cut - at > hard and last > at:
            bounds.append((at, last))
            at = last
        last = cut
    bounds.append((at, end))
    return [Chunk(s, e, text[s:e], split=True) for s, e in bounds if text[s:e].strip()]


QUOTES = str.maketrans({"‘": "'", "’": "'", "“": '"', "”": '"', "–": "-", "—": "-", "⁄": "/"})


def normalize(text: str) -> str:
    """Entities decoded, compatibility forms folded (½ is 1/2), quotes and dashes plain, case and spacing ignored."""
    text = unicodedata.normalize("NFKC", html.unescape(text or "")).translate(QUOTES).lower()
    return re.sub(r"\s+", " ", text).strip().strip(".,;:")


NUMBER_WORDS = {
    "one": 1, "two": 2, "three": 3, "four": 4, "five": 5, "six": 6, "seven": 7, "eight": 8, "nine": 9, "ten": 10,
    "eleven": 11, "twelve": 12, "half": Fraction(1, 2), "dozen": 12,
    "uno": 1, "una": 1, "un": 1, "due": 2, "tre": 3, "quattro": 4, "cinque": 5, "sei": 6, "sette": 7, "otto": 8,
    "nove": 9, "dieci": 10, "undici": 11, "dodici": 12, "mezzo": Fraction(1, 2), "mezza": Fraction(1, 2), "dozzina": 12,
}

NUMERIC = re.compile(r"(?P<whole>\d+)\s+(?P<num>\d+)/(?P<den>\d+)|(?P<n2>\d+)/(?P<d2>\d+)|(?P<plain>\d+(?:[.,]\d+)?)")


def numbers(text: str, words: bool = True) -> Counter:
    """Every number the text states, as exact fractions: digits, fractions (½ is 1/2), mixed numbers and,
    unless words is False, number words in English and Italian ("four", "quattro", "mezzo")."""
    folded = normalize(text)
    found: Counter = Counter()
    for m in NUMERIC.finditer(folded):
        if m.group("whole"):
            found[Fraction(int(m.group("whole"))) + Fraction(int(m.group("num")), int(m.group("den")))] += 1
        elif m.group("n2"):
            if int(m.group("d2")):
                found[Fraction(int(m.group("n2")), int(m.group("d2")))] += 1
        else:
            found[Fraction(m.group("plain").replace(",", "."))] += 1
    if words:
        for word in re.findall(r"[^\W\d_]+", folded):
            if word in NUMBER_WORDS:
                found[Fraction(NUMBER_WORDS[word])] += 1
    return found


UNITS = [
    ("kg", r"kilo(?:gram|gramme)s?|kgs?|chilogramm[io]|chilo"),
    ("g", r"grams?|grammes?|g|gr|grammi|grammo"),
    ("dl", r"decilit(?:er|re)s?|dl|decilitr[io]"),
    ("l", r"lit(?:er|re)s?|l|litr[io]"),
    ("tbsp", r"tablespoon(?:ful)?s?|tbsps?|cucchiai[oe]?|cucchiaiat[ae]"),
    ("tsp", r"teaspoon(?:ful)?s?|tsps?|cucchiain[io]"),
    ("glass", r"glass(?:es|ful|fuls)?|bicchier[ie]"),
    ("cup", r"cups?|tazz[ae]"),
    ("pinch", r"pinch(?:es)?|pizzic[oh]i?"),
    ("clove", r"cloves?|spicch[io]o?"),
]
UNIT_PATTERNS = [(name, re.compile(r"(?<![^\W\d_])(?:" + pattern + r")(?![^\W\d_])\.?", re.I)) for name, pattern in UNITS]


def units(text: str) -> Counter:
    """The measures the text names, by one name in either language (grammi and grams are g)."""
    folded = normalize(text)
    found: Counter = Counter()
    taken = [False] * len(folded)
    for name, pattern in UNIT_PATTERNS:
        for m in pattern.finditer(folded):
            if any(taken[m.start():m.end()]):
                continue
            for i in range(m.start(), m.end()):
                taken[i] = True
            found[name] += 1
    return found


def content_words(text: str) -> list[str]:
    return [w for w in re.findall(r"[^\W\d_]{3,}", normalize(text))]


def fidelity(line: str, source: str) -> str | None:
    """Why an extracted line is not faithful to its source text, or None: it appears in the source after
    normalization, or each of its numbers appears in the source and each of its words is found there."""
    n_line, n_source = normalize(line), normalize(source)
    if not n_line:
        return "empty line"
    if n_line in n_source:
        return None
    missing_numbers = numbers(line, words=False) - numbers(source, words=False)
    if missing_numbers:
        return "a number not in the book: " + ", ".join(str(n) for n in missing_numbers)
    source_words = set(content_words(source))
    missing = [w for w in content_words(line) if w not in source_words]
    if missing:
        return "words not in the book: " + ", ".join(missing[:5])
    return None


def number_check(english: str, italian: str) -> str | None:
    """Why an English line does not carry exactly the numbers and units of the Italian line it translates, or None.
    One is not compared: an article ("a", "una", "uno") reads as one in one language and not in the other."""
    en_n, it_n = set(numbers(english)) - {Fraction(1)}, set(numbers(italian)) - {Fraction(1)}
    if en_n != it_n:
        return f"numbers {sorted(str(n) for n in en_n)} against the Italian {sorted(str(n) for n in it_n)}"
    en_u, it_u = set(units(english)), set(units(italian))
    if en_u != it_u:
        return f"units {sorted(en_u)} against the Italian {sorted(it_u)}"
    return None


@dataclass
class Result:
    kept: list[dict] = field(default_factory=list)
    rejects: list[dict] = field(default_factory=list)
    chunks: int = 0
    split_chunks: int = 0
    unreadable_chunks: int = 0

    def summary(self) -> dict:
        reasons = Counter(r["reason"].split(":")[0] for r in self.rejects)
        return {
            "chunks": self.chunks, "splitChunks": self.split_chunks, "unreadableChunks": self.unreadable_chunks,
            "kept": len(self.kept), "dropped": len(self.rejects), "droppedByReason": dict(sorted(reasons.items())),
            "pinned": sum(1 for r in self.kept if "replacesRecipeId" in r),
        }


EXTRACT_PROMPT = (
    "You copy recipes out of an old cookbook text. Return every complete recipe in the text. For each recipe give its title "
    "exactly as printed, every ingredient line exactly as printed (one line per ingredient, in the book's order, copied character "
    "for character, never converting, adding or dropping an amount) and the method as printed, split into its sentences or "
    "paragraphs. Leave out a recipe the text cuts off. Reply with STRICT JSON only: "
    '{"recipes":[{"name":"...","ingredients":["..."],"steps":["..."]}]}'
)

TRANSLATE_PROMPT = (
    "You translate recipes from Pellegrino Artusi's Italian cookbook into plain English. Return every complete recipe in the text. "
    "For each recipe give its Italian title exactly as printed (sourceName), an English title (name), and every ingredient and step "
    "as a pair: the English text and the Italian line it translates, copied exactly from the book (source). Keep every number and "
    "every unit exactly as the Italian states it (grammi is grams, chilogrammi kilograms, litro a litre, decilitro a decilitre, "
    "cucchiaio a tablespoon, cucchiaino a teaspoon, bicchiere a glass); never convert, round, add or drop an amount, and name no "
    "amount the Italian does not state. One English ingredient line per Italian ingredient line. Reply with STRICT JSON only: "
    '{"recipes":[{"sourceName":"...","name":"...","ingredients":[{"text":"...","source":"..."}],"steps":[{"text":"...","source":"..."}]}]}'
)


def ollama(host: str, model: str, timeout: int = 600) -> Ask:
    """A model call to the operator's Ollama host: system prompt and chunk in, the reply's text out."""
    def ask(system: str, user: str) -> str:
        body = {"model": model, "stream": False, "format": "json", "options": {"temperature": 0, "num_ctx": 16384},
                "messages": [{"role": "system", "content": system}, {"role": "user", "content": user}]}
        request = urllib.request.Request(host.rstrip("/") + "/api/chat", data=json.dumps(body).encode(), method="POST",
                                         headers={"Content-Type": "application/json", "User-Agent": USER_AGENT})
        with urllib.request.urlopen(request, timeout=timeout) as response:
            return json.loads(response.read().decode())["message"]["content"]
    return ask


def _pairs(items) -> list[tuple[str, str | None]]:
    out = []
    for item in items or []:
        if isinstance(item, str):
            out.append((item, None))
        elif isinstance(item, dict) and isinstance(item.get("text"), str):
            out.append((item["text"], item.get("source") if isinstance(item.get("source"), str) else None))
    return out


def check_record(recipe: dict, chunk: Chunk, translate: bool) -> tuple[dict | None, str | None]:
    """The scraper record of one model answer, or why it is dropped (rules 3 and 4)."""
    name = recipe.get("name") if isinstance(recipe.get("name"), str) else None
    ingredients = _pairs(recipe.get("ingredients"))
    steps = _pairs(recipe.get("steps"))
    if not name or not ingredients:
        return None, "no name or no ingredient line"
    if translate:
        sources = [source for _, source in ingredients]
        if any(not s for s in sources):
            return None, "an ingredient line quotes no Italian line"
        if len(set(normalize(s) for s in sources)) != len(sources):
            return None, "two English lines translate one Italian line"
        for text, source in ingredients + [(t, s) for t, s in steps if s]:
            if normalize(source) not in normalize(chunk.text):
                return None, f"an Italian line not in the book: {source[:60]}"
            problem = number_check(text, source)
            if problem:
                return None, f"number check: {text[:60]}: {problem}"
        if any(not s for _, s in steps):
            return None, "a step quotes no Italian line"
    else:
        for text, _ in ingredients:
            problem = fidelity(text, chunk.text)
            if problem:
                return None, f"fidelity: {text[:60]}: {problem}"
    record = {
        "name": name.strip(),
        "publicDomain": True,
        "ingredients": [{"rawLine": text.strip()} for text, _ in ingredients],
        "steps": [{"order": i + 1, "instruction": text.strip()} for i, (text, _) in enumerate(steps)],
    }
    if translate and isinstance(recipe.get("sourceName"), str):
        record["sourceName"] = recipe["sourceName"].strip()
    if chunk.split:
        record["split"] = True
    return record, None


def extract(text: str, ask: Ask, translate: bool = False, limit: int = DEFAULT_LIMIT, hard: int = HARD_LIMIT) -> Result:
    """Every chunk asked once; every answer checked by rule; kept records and dropped ones with their reasons."""
    result = Result()
    prompt = TRANSLATE_PROMPT if translate else EXTRACT_PROMPT
    for chunk in chunks(text, limit, hard):
        result.chunks += 1
        if chunk.split:
            result.split_chunks += 1
        try:
            answer = json.loads(ask(prompt, chunk.text))
            recipes = answer.get("recipes") if isinstance(answer, dict) else None
            if not isinstance(recipes, list):
                raise ValueError("no recipes list")
        except (ValueError, json.JSONDecodeError) as exc:
            result.unreadable_chunks += 1
            log(f"chunk at {chunk.start}: unreadable answer ({exc}); its recipes wait for the next run")
            continue
        for recipe in recipes:
            if not isinstance(recipe, dict):
                continue
            record, reason = check_record(recipe, chunk, translate)
            if record is None:
                result.rejects.append({"chunk": chunk.start, "name": recipe.get("name"), "reason": reason})
            else:
                result.kept.append(record)
    return result


def load_pins(recipe_map: str | None, old_file: str | None, pin_csv: str | None) -> dict[str, int]:
    """Normalized recipe name to recipe id: from an attach log (lines of old_file whose record matched or was already
    attached), then from a CSV of name,recipeId, which wins. A name two recipes share is left out."""
    pins: dict[str, int] = {}
    doubled: set[str] = set()
    if recipe_map:
        with open(recipe_map, encoding="utf-8") as handle:
            for text in handle:
                if not text.strip():
                    continue
                entry = json.loads(text)
                if old_file and entry.get("file") != old_file:
                    continue
                if entry.get("outcome") in MATCHED and entry.get("recipeId") and entry.get("name"):
                    key = normalize(entry["name"])
                    if key in pins and pins[key] != entry["recipeId"]:
                        doubled.add(key)
                    pins[key] = int(entry["recipeId"])
    for key in doubled:
        pins.pop(key, None)
    if pin_csv:
        with open(pin_csv, encoding="utf-8", newline="") as handle:
            for row in csv.DictReader(handle):
                if row.get("name") and row.get("recipeId"):
                    pins[normalize(row["name"])] = int(row["recipeId"])
    return pins


def pin(records: Iterable[dict], pins: dict[str, int]) -> int:
    """Names the recipe each record replaces, by its name or the title it quotes; returns how many were pinned."""
    count = 0
    for record in records:
        for key in (record.get("name"), record.get("sourceName")):
            if key and normalize(key) in pins:
                record["replacesRecipeId"] = pins[normalize(key)]
                count += 1
                break
    return count


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Extract a cookbook's recipes again, with recipe-boundary chunks and rule checks (no fetching).")
    parser.add_argument("book", help="the book's text (pg*.txt), read locally")
    parser.add_argument("--host", required=True, help="the Ollama host the operator names")
    parser.add_argument("--model", required=True)
    parser.add_argument("--translate", action="store_true", help="the book is Italian (Artusi): translate with the number check")
    parser.add_argument("--limit", type=int, default=DEFAULT_LIMIT)
    parser.add_argument("--hard-limit", type=int, default=HARD_LIMIT)
    parser.add_argument("--out", required=True, help="JSONL of kept records")
    parser.add_argument("--rejects", help="JSONL of dropped records with their reasons")
    parser.add_argument("--recipe-map", help="the attach log of the book's first attach")
    parser.add_argument("--old-file", help="the staging file name the first attach read, to pick its lines from the log")
    parser.add_argument("--pin-csv", help="CSV with columns name,recipeId")
    args = parser.parse_args(argv)

    with open(args.book, encoding="utf-8", errors="replace") as handle:
        text = handle.read().replace("\r\n", "\n")
    result = extract(text, ollama(args.host, args.model), translate=args.translate, limit=args.limit, hard=args.hard_limit)
    pinned = pin(result.kept, load_pins(args.recipe_map, args.old_file, args.pin_csv))
    with open(args.out, "w", encoding="utf-8") as handle:
        for record in result.kept:
            handle.write(json.dumps(record, ensure_ascii=False) + "\n")
    if args.rejects:
        with open(args.rejects, "w", encoding="utf-8") as handle:
            for reject in result.rejects:
                handle.write(json.dumps(reject, ensure_ascii=False) + "\n")
    summary = result.summary()
    summary["pinned"] = pinned
    print(json.dumps(summary, indent=2))
    return 0 if result.kept else 2


if __name__ == "__main__":
    sys.exit(main())

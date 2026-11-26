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
3. Fidelity check, rule-given, on every record (no model), against the record's own recipe in the
   chunk (from the title line that names it to the next title; the whole chunk when it holds one
   recipe): every ingredient line must appear in that recipe after normalization (entities,
   spacing, case, quotes); otherwise one line or sentence of it must hold every number and every
   word of the line. Every step must appear in the recipe, or one sentence of it must hold every
   number (number words included) and every word of the step, so the method a rebuild fills
   amounts from states no amount the book does not. Every ingredient line of the recipe's
   ingredient list (a paragraph of short lines that states amounts) must be extracted. A record
   that fails is dropped and written to --rejects with its reason, never to --out. A line that
   the book wraps (an entry of a list set over several lines, a word broken at a line end) is
   found joined. A mixed number is one number however its fraction is set ("1¼", "1 ¼", "1 1/4").
3a. Readability (the hand import of OCR'd books, which passes a word list): every ingredient line
   and step must read as English: three in four of its letter tokens known words (SCOWL's
   american-english, lower-case entries, and a short list of old cookery words), no token that
   mixes figures and letters other than an ordinal or a unit glued to its number, and a known word
   that names something. OCR garbage copied verbatim passes the fidelity check; it fails this one.
   The same test marks a recipe whose short lines are mostly unreadable, so it is never sent to
   the model (read_by_rule).
4. Translation (--translate, for Artusi's "La scienza in cucina"): the model writes English and
   quotes, for each ingredient line and step, the Italian it translates. The quote must lie in the
   recipe and name a food, not only an amount; every number and unit of the English must equal one
   in the quote (grammi is grams, chilogrammi kilograms, litro a litre, cucchiaio a tablespoon and
   so on), none may be added or dropped; the English and the quote must name the same common foods
   (burro and butter, zucchero and sugar and so on), so an amount is not moved to another food;
   each English ingredient line must come from an Italian line of its own, and every line of the
   recipe's ingredient list must be translated with its whole amount. The model supplies no
   amount: every amount already stands in the source. A record that fails is dropped and logged.
5. Each record may name the recipe whose source it replaces ("replacesRecipeId"), from the log of
   the first attach of the book (--recipe-map, matched by the title it quotes, else by its name)
   or from a CSV of name to recipe id (--pin-csv). The attach script sends it as a pin. A record
   whose quoted title and name point to two recipes, and every record of a recipe two records
   point to, is left unpinned and logged.

6. A slow or busy model host loses nothing: every readable answer is written to a checkpoint file
   (--checkpoint, default <out>.answers.jsonl) as it arrives and read back on the next run with the
   same --model, so a chunk that model answered is never asked twice; an unreadable answer is not
   kept, so its chunk is asked again on the next run; a call past --timeout seconds or a failed one is asked again up to
   --retries times, and a chunk that still gets no answer is counted (failedChunks) and waits for
   the next run, while every record already answered is kept and written.

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
import hashlib
import html
import json
import os
import re
import sys
import time
import unicodedata
import urllib.error
import urllib.request
from collections import Counter
from dataclasses import dataclass, field
from fractions import Fraction
from typing import Callable, Iterable

DEFAULT_LIMIT = 4000
HARD_LIMIT = 12000
USER_AGENT = "nom-cookbook-extract/1.0"
OLLAMA_OPTIONS = {"temperature": 0, "num_ctx": 16384}
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
VULGAR = "½¼¾⅓⅔⅛⅜⅝⅞⅕⅖⅗⅘⅙⅚"


def normalize(text: str) -> str:
    """Entities decoded, compatibility forms folded (½ is 1/2), quotes and dashes plain, case and spacing ignored. A
    fraction sign after a whole number is a mixed number whatever stands between ("1¼", "1 ¼" with a no-break space and
    "1 1/4" are all 1 1/4: staging checkpoint 7, S23, "1¼" read as 11/4 against the model's 5/4), and a fraction set
    with raised figures ("¹/ 8") is closed up."""
    text = re.sub(rf"(\d)[\s   ]*([{VULGAR}])", r"\1 \2", html.unescape(text or ""))
    text = unicodedata.normalize("NFKC", text).translate(QUOTES).lower()
    text = re.sub(r"(\d)\s*/\s*(\d)", r"\1/\2", text)
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
    ("oz", r"ounces?|oz|onci[ae]"),
    ("lb", r"pounds?|lbs?|libbr[ae]"),
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


LIST_START = re.compile(r"^\s*(?:[\d½¼¾⅓⅔⅛⅜⅝⅞]|(?:a|an|one|two|three|four|five|six|seven|eight|nine|ten|twelve|half|pinch|dash)\b)", re.I)


DANGLING_END = re.compile(r"(?:\b(?:into|or|and|to|about|of|with|for|plus|from)|[-–(,])\s*$", re.I)


def wrapped_entries(source: str) -> list[str]:
    """The entries of the text's lists with their wrapped lines joined: inside a paragraph a line that starts no amount
    continues the line before ("1½ tablespoon" / "Hot 'n Spicy Seasoning" / "(see recipe on page 19)": staging
    checkpoint 7, S23, a record dropped because its line is set over three lines of a narrow column), and so does any
    line after one that cannot end an entry ("boned, and cut into" / "1-inch strips")."""
    out = []
    for paragraph in re.split(r"\n\s*\n", source):
        lines = [line.strip() for line in paragraph.split("\n") if line.strip()]
        entries: list[str] = []
        for line in lines:
            if entries and (not LIST_START.match(line) or DANGLING_END.search(entries[-1])):
                entries[-1] = f"{entries[-1]} {line}"
            else:
                entries.append(line)
        out.extend(entry for entry in entries if entry not in lines)
    return out


def pieces(source: str) -> list[str]:
    """The lines and sentences of a recipe's text, normalized: each line, each list entry with its wrapped lines, and
    each sentence or clause of each paragraph of prose (one with a line longer than a list line)."""
    out = [normalize(line) for line in source.split("\n")]
    out.extend(normalize(entry) for entry in wrapped_entries(source))
    for paragraph in re.split(r"\n\s*\n", source):
        if any(len(line.strip()) > 60 for line in paragraph.split("\n")):
            out.extend(re.split(r"(?<=[.;:!?])\s+", normalize(paragraph)))
    return [p for p in out if p]


LINE_END_BREAK = re.compile(r"(?<=[^\W\d_])-[ \t]*\n[ \t]*(?=[^\W\d_])")
INNER_HYPHEN = re.compile(r"(?<=[^\W\d_])-(?=[^\W\d_])")


def _fidelity(line: str, source: str, number_words: bool) -> str | None:
    n_line = normalize(line)
    if not n_line:
        return "empty line"
    if n_line in normalize(source):
        return None
    wanted = numbers(line, words=number_words)
    missing_numbers = wanted - numbers(source, words=number_words)
    if missing_numbers:
        return "a number not in the book: " + ", ".join(str(n) for n in missing_numbers)
    source_words = set(content_words(source))
    missing = [w for w in content_words(line) if w not in source_words]
    if missing:
        return "words not in the book: " + ", ".join(missing[:5])
    words = set(content_words(line))
    for piece in pieces(source):
        if not wanted - numbers(piece, words=number_words) and words <= set(content_words(piece)):
            return None
    return "no line or sentence of the recipe holds its numbers and words together"


def fidelity(line: str, source: str, number_words: bool = False) -> str | None:
    """Why an extracted line is not faithful to its source text, or None: it appears in the source after
    normalization, or one line, list entry or sentence of the source holds each of its numbers and each of its words.
    A word the book breaks at a line end is found joined, with or without its hyphen ("beat-" / "en" is "beaten",
    "well-" / "beaten" is "well-beaten"): where the source breaks a word, the line and the source are compared again
    with the breaks joined and the hyphens inside words left out."""
    problem = _fidelity(line, source, number_words)
    if problem is None or not LINE_END_BREAK.search(source):
        return problem
    joined = INNER_HYPHEN.sub("", LINE_END_BREAK.sub("", source))
    return None if _fidelity(INNER_HYPHEN.sub("", line), joined, number_words) is None else problem


WORD_LISTS = ("/usr/share/dict/american-english", "/usr/share/dict/words")
COOKERY_MEASURES = frozenset("""
a i n s qt qts pt pts lb lbs oz tsp tsps tbsp tbsps doz gal gals pk pkg bbl cupful cupfuls spoonful spoonfuls teaspoonfuls
tablespoonfuls saltspoon saltspoonful saltspoonfuls dessertspoonful gill gills
""".split())
COOKERY_WORDS = COOKERY_MEASURES | frozenset("""
saleratus pearlash graham sweetbreads
cornstarch cornmeal shortening oleo oleomargarine crisco cottolene sifted unsifted scant heaping rounded level stoned
seeded blanched dredge dredged lard suet citron mace allspice nutmeg cloves zwieback farina tapioca sago gelatine
isinglass rennet junket pimento pimentos pimientos catsup ketchup worcestershire paprika cayenne tabasco bouillon
consomme croutons sippets forcemeat giblets chitterlings scrapple hominy samp succotash johnnycake jonnycake flapjacks
griddle griddlecakes doughnuts crullers jumbles snaps wafers biscuit biscuits rusk rusks gems muffins popovers
""".split())
NOT_WORD_SYMBOLS = re.compile(r"[|\\{}<>~^_=#@¥§¢£€]")
UNREAD = "{unread}"
ORDINAL = re.compile(r"^\d+(?:st|nd|rd|th|d)$", re.I)
NUMBERISH = re.compile(r"^\d+(?:[/.,:]\d+)*$")
UNIT_AFTER_NUMBER = re.compile(r"^\d+(?:\.\d+)?(?:in|oz|lb|lbs|qt|qts|pt|pts|g|mg|mcg|kg|ml|x|f|c)$", re.I)
NOT_CONTENT = frozenset("""
a an and are as at be but by for from in into is it its of on or so the then this to up with when until till cup cups pint
pints quart quarts pound pounds ounce ounces teaspoon teaspoons tablespoon tablespoons spoon spoons gill gills dozen
""".split()) | COOKERY_MEASURES


def load_words(paths: Iterable[str] | None = None) -> frozenset[str] | None:
    """The English words the readability test knows: the first word list found (SCOWL's american-english, Debian's
    wamerican, in the nom-hand-import image), its lower-case entries only, so a name ("Eli", "Cecil") is no word, and a
    short list of old cookery words; None when no list is found."""
    for path in WORD_LISTS if paths is None else paths:
        if path and os.path.exists(path):
            with open(path, encoding="utf-8", errors="replace") as handle:
                found = {w.strip() for w in handle if w.strip() and w.strip().islower() and "'" not in w}
            return frozenset(found) | COOKERY_WORDS
    return None


def known_word(word: str, words: frozenset[str] | set[str]) -> bool:
    """Whether a lower-case word is in the list, as it stands, as a plural, with "un" before a known word ("unpeeled"),
    or as two known words of three letters or more run together ("cornflake", "dillweed")."""
    if word in words or (len(word) > 3 and word.endswith("s") and word[:-1] in words):
        return True
    if len(word) > 4 and word.startswith("un") and word[2:] in words:
        return True
    return len(word) >= 6 and any(word[:i] in words and word[i:] in words for i in range(3, len(word) - 2))


def unreadable(text: str, words: frozenset[str] | set[str], ingredient: bool = False) -> str | None:
    """Why a line of OCR text is not readable English, or None (staging checkpoint 7, S22: the Library's OCR of a
    dot-leader list, "ELI aa a as I pint", passed the number check because the record copied it). Dot leaders and
    numbers aside, at least three in four of its letter tokens must be known words (a lone letter only "a", "I", "n");
    a token that mixes figures and letters ("0Ut", "1ae") other than an ordinal or a unit glued to its number, and a
    stray symbol ("|", "§"), count as unknown; and it must hold a known word of three letters or more that is not a
    measure (for an ingredient line: one that names something besides the amount)."""
    cleaned = re.sub(r"(?:[.·…_-]\s*){3,}", " ", unicodedata.normalize("NFKC", text).replace("°", " "))
    known, unknown, content = 0, 0, 0
    unknown += len(NOT_WORD_SYMBOLS.findall(cleaned))
    for raw in re.split(r"[\s\-–—/⁄]+", cleaned):
        token = raw.strip("\"'‘’“”.,;:!?()[]*%™®©")
        token = re.sub(r"['’]s$", "", token)
        if not token or NUMBERISH.match(token):
            continue
        if re.search(r"\d", token):
            if ORDINAL.match(token) or UNIT_AFTER_NUMBER.match(token):
                known += 1
            else:
                unknown += 1
            continue
        letters = token.lower().strip("'’")
        if not re.fullmatch(r"[^\W\d_]+(?:['’][^\W\d_]+)?", letters):
            unknown += 1
            continue
        plain = re.sub(r"['’]", "", letters)
        if known_word(letters, words) or known_word(plain, words):
            known += 1
            if len(plain) >= 3 and plain not in NOT_CONTENT:
                content += 1
        else:
            unknown += 1
    total = known + unknown
    if total == 0:
        return None if not ingredient else "no word"
    if known < 0.75 * total and not (total <= 4 and unknown == 1 and known >= 2):
        return f"unreadable text: {known} of {total} words known"
    if content == 0 and (ingredient or total >= 3):
        return "unreadable text: no known word that names anything"
    return None


def unreadable_share(text: str, words: frozenset[str] | set[str]) -> tuple[int, int]:
    """The lines of a page that hold letters, and how many of them are not readable."""
    lines = [line for line in text.split("\n") if re.search(r"[^\W\d_]", line)]
    return len(lines), sum(1 for line in lines if unreadable(line, words))


def listed_lines(source: str, translate: bool) -> list[str]:
    """The lines of the recipe's ingredient lists: paragraphs of short lines with no title among them, of which at
    least one states an amount (a line that starts with one; in Italian, any number)."""
    lines = source.split("\n")
    titles = {lines[i].strip() for i in range(len(lines)) if is_title(lines, i)}
    out = []
    for block in re.split(r"\n\s*\n", source):
        rows = [line.strip() for line in block.split("\n") if line.strip()]
        if not rows or any(len(row) > 60 or row in titles for row in rows):
            continue
        if any(numbers(row) if translate else INGREDIENT_START.match(row) for row in rows):
            out.extend(rows)
    return out


def recipe_text(chunk: Chunk, *names: str | None) -> str | None:
    """The record's own recipe in the chunk: from the title line that names it to the next title. The whole
    chunk when it holds one recipe or a piece of one; None when it holds several and no title names the record."""
    starts = sorted(set([0] + title_offsets(chunk.text)))
    blocks = [chunk.text[s:e] for s, e in zip(starts, starts[1:] + [len(chunk.text)]) if chunk.text[s:e].strip()]
    if chunk.split or len(blocks) <= 1:
        return chunk.text
    keys = [normalize(n) for n in names if isinstance(n, str) and normalize(n)]
    titled = [(normalize(block.strip().split("\n", 1)[0]), block) for block in blocks]
    for key in keys:
        exact = [block for title, block in titled if title == key or re.sub(r"^\d{1,4}\.\s*", "", title) == key]
        if len(exact) == 1:
            return exact[0]
        near = [block for title, block in titled if title and (key in title or title in key)]
        if len(near) == 1:
            return near[0]
    return None


FOODS = [
    (r"burro", r"burro", r"butter"),
    (r"zucchero", r"zucchero", r"sugar"),
    (r"farina", r"farin[ae]|fecola", r"flour"),
    (r"uov[aoi]|tuorl[oi]", r"uov[aoi]|tuorl[oi]|ross[oi]|chiar[ae]", r"eggs?|yolks?"),
    (r"latte", r"latte", r"milk"),
    (r"riso", r"riso", r"rice"),
    (r"olio", r"olio", r"oil"),
    (r"aglio", r"aglio", r"garlic"),
    (r"cipoll[ae]", r"cipoll[ae]", r"onions?"),
    (r"pomodor[oi]", r"pomodor[oi]", r"tomato(?:es)?"),
    (r"patat[ae]", r"patat[ae]", r"potato(?:es)?"),
    (r"prezzemolo", r"prezzemolo", r"parsley"),
    (r"limon[ei]", r"limon[ei]", r"lemons?"),
    (r"parmigiano", r"parmigiano", r"parmesan"),
]
SALT = (r"sale", r"sale", r"salt")
NOT_FOOD = {"circa", "per", "con", "del", "della", "delle", "dei", "degli", "dello", "nel", "nella", "una", "uno", "due", "tre",
            "mezzo", "mezza", "the", "and", "about", "half"}


def food_check(english: str, italian: str, ingredient: bool) -> str | None:
    """Why the English and the Italian it translates name different common foods, or None. A food the Italian
    names must be named in the English and the other way round (burro and butter, uova and eggs)."""
    en, it = normalize(english), normalize(italian)
    for strict, loose, words in FOODS + ([SALT] if ingredient else []):
        if re.search(r"\b(?:" + strict + r")\b", it) and not re.search(r"\b(?:" + words + r")\b", en):
            return f"the Italian names {re.search(r'(?:' + strict + r')', it).group(0)} and the English does not"
        if re.search(r"\b(?:" + words + r")\b", en) and not re.search(r"\b(?:" + loose + r")\b", it):
            return f"the English names {re.search(r'(?:' + words + r')', en).group(0)} and the Italian does not"
    return None


def names_food(italian: str) -> bool:
    """Whether a quoted Italian line names something besides an amount: a word that is not a number or a measure."""
    folded = normalize(italian)
    for _, pattern in UNIT_PATTERNS:
        folded = pattern.sub(" ", folded)
    return any(w not in NUMBER_WORDS and w not in NOT_FOOD for w in content_words(folded))


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
    failed_chunks: int = 0

    def summary(self) -> dict:
        reasons = Counter(r["reason"].split(":")[0] for r in self.rejects)
        return {
            "chunks": self.chunks, "splitChunks": self.split_chunks, "unreadableChunks": self.unreadable_chunks,
            "failedChunks": self.failed_chunks,
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
        body = {"model": model, "stream": False, "format": "json", "options": OLLAMA_OPTIONS,
                "messages": [{"role": "system", "content": system}, {"role": "user", "content": user}]}
        request = urllib.request.Request(host.rstrip("/") + "/api/chat", data=json.dumps(body).encode(), method="POST",
                                         headers={"Content-Type": "application/json", "User-Agent": USER_AGENT})
        with urllib.request.urlopen(request, timeout=timeout) as response:
            return json.loads(response.read().decode())["message"]["content"]
    return ask


class ModelCallFailed(Exception):
    """A model call that did not answer after its retries (a timeout or a refused connection)."""


class ModelCallTimedOut(ModelCallFailed):
    """A model call that ran past its timeout once, with timeouts not asked again."""


MODEL_ERRORS = (TimeoutError, OSError, urllib.error.URLError, ConnectionError, json.JSONDecodeError, KeyError)


def timed_out(exc: BaseException) -> bool:
    """Whether a failed model call ran past its timeout (a socket timeout, also when urllib wraps it)."""
    return isinstance(exc, TimeoutError) or isinstance(getattr(exc, "reason", None), TimeoutError)


def readable(answer: str) -> bool:
    """Whether an answer is one the extraction can read: a JSON object with a list of recipes."""
    try:
        parsed = json.loads(answer)
    except (ValueError, TypeError):
        return False
    return isinstance(parsed, dict) and isinstance(parsed.get("recipes"), list)


def resumable(ask: Ask, checkpoint: str | None, retries: int = 2, wait: float = 30.0, sleep: Callable[[float], None] = time.sleep,
              model: str = "", retry_timeouts: bool = True) -> Ask:
    """A model call that survives a slow or busy host: every readable answer is appended to the checkpoint file (JSON
    lines keyed by the SHA-256 of the model, the prompt and the chunk) the moment it arrives, a chunk the same model
    answered readably before is read back from it and not asked again, and a call that times out or fails is retried up
    to `retries` times, `wait` seconds apart (doubling). An unreadable answer is returned but never kept, so the next
    run asks for that chunk again, and another model's answers are never reused. A call that still fails raises
    ModelCallFailed, which the caller counts as a failed chunk and goes on: no answered record is lost, and the next run
    asks only the chunks still missing (staging checkpoint 3, N31; review of ck6-data-fixes, CK6-6). With
    retry_timeouts off, a call that times out is not asked again in this run: one long timeout drops the chunk
    (ModelCallTimedOut) and the run goes on (staging checkpoint 4, P34)."""
    answers: dict[str, str] = {}
    if checkpoint and os.path.exists(checkpoint):
        with open(checkpoint, encoding="utf-8") as handle:
            for text in handle:
                try:
                    row = json.loads(text)
                except json.JSONDecodeError:
                    continue
                if isinstance(row, dict) and isinstance(row.get("key"), str) and isinstance(row.get("answer"), str) and readable(row["answer"]):
                    answers[row["key"]] = row["answer"]

    def call(system: str, user: str) -> str:
        key = answer_key(model, system, user)
        if key in answers:
            return answers[key]
        problem = None
        for attempt in range(retries + 1):
            if attempt:
                sleep(wait * (2 ** (attempt - 1)))
            try:
                answer = ask(system, user)
            except MODEL_ERRORS as exc:
                problem = exc
                log(f"model call failed (try {attempt + 1} of {retries + 1}): {type(exc).__name__}: {exc}")
                if not retry_timeouts and timed_out(exc):
                    raise ModelCallTimedOut(f"{type(exc).__name__}: {exc}; not asked again this run") from exc
                continue
            if not readable(answer):
                return answer
            answers[key] = answer
            if checkpoint:
                with open(checkpoint, "a", encoding="utf-8") as handle:
                    handle.write(json.dumps({"key": key, "model": model, "answer": answer}, ensure_ascii=False) + "\n")
            return answer
        raise ModelCallFailed(f"{type(problem).__name__}: {problem} after {retries + 1} tries")

    return call


def _pairs(items) -> list[tuple[str, str | None]]:
    out = []
    for item in items or []:
        if isinstance(item, str):
            out.append((item, None))
        elif isinstance(item, dict) and isinstance(item.get("text"), str):
            out.append((item["text"], item.get("source") if isinstance(item.get("source"), str) else None))
    return out


def readability_check(ingredients: Iterable[str], steps: Iterable[str], words: frozenset[str] | set[str]) -> str | None:
    """Why a record's text is not readable English, or None: every ingredient line and every step must pass the
    readability test (rule 3a)."""
    for text in ingredients:
        problem = unreadable(text, words, ingredient=True)
        if problem:
            return f"{problem}: {text[:60]}"
    for text in steps:
        problem = unreadable(text, words)
        if problem:
            return f"{problem}: {text[:60]}"
    return None


def check_record(recipe: dict, chunk: Chunk, translate: bool, words: frozenset[str] | set[str] | None = None) -> tuple[dict | None, str | None]:
    """The scraper record of one model answer, or why it is dropped (rules 3 and 4; with a word list, also rule 3a)."""
    name = recipe.get("name") if isinstance(recipe.get("name"), str) else None
    ingredients = _pairs(recipe.get("ingredients"))
    steps = _pairs(recipe.get("steps"))
    if not name or not ingredients:
        return None, "no name or no ingredient line"
    source_name = recipe.get("sourceName") if isinstance(recipe.get("sourceName"), str) else None
    own = recipe_text(chunk, source_name, name) if translate else recipe_text(chunk, name)
    if own is None:
        return None, "no title of the book text names the recipe"
    if translate:
        sources = [source for _, source in ingredients]
        if any(not s for s in sources):
            return None, "an ingredient line quotes no Italian line"
        if len(set(normalize(s) for s in sources)) != len(sources):
            return None, "two English lines translate one Italian line"
        if any(not s for _, s in steps):
            return None, "a step quotes no Italian line"
        for kind, pairs in (("ingredient", ingredients), ("step", steps)):
            for text, source in pairs:
                if normalize(source) not in normalize(own):
                    return None, f"an Italian line not in the book: {source[:60]}"
                if kind == "ingredient" and not names_food(source):
                    return None, f"an Italian line that names no food: {source[:60]}"
                problem = number_check(text, source) or food_check(text, source, kind == "ingredient")
                if problem:
                    return None, f"number check: {text[:60]}: {problem}"
        for line in listed_lines(own, translate=True):
            whole = set(numbers(line)) - {Fraction(1)}
            if not any(normalize(s) in normalize(line) and set(numbers(s)) - {Fraction(1)} == whole for s in sources):
                return None, f"an Italian ingredient line not translated: {line[:60]}"
    else:
        for text, _ in ingredients:
            problem = fidelity(text, own)
            if problem:
                return None, f"fidelity: {text[:60]}: {problem}"
        for text, _ in steps:
            problem = fidelity(text, own, number_words=True)
            if problem:
                return None, f"fidelity: {text[:60]}: {problem}"
        extracted = [normalize(text) for text, _ in ingredients]
        for line in listed_lines(own, translate=False):
            folded, stated = normalize(line), numbers(line, words=False)
            if not any((e in folded or folded in e) and numbers(e, words=False) == stated for e in extracted):
                return None, f"an ingredient line not extracted: {line[:60]}"
        if words is not None:
            problem = readability_check((t for t, _ in ingredients), (t for t, _ in steps), words)
            if problem:
                return None, problem
    record = {
        "name": name.strip(),
        "publicDomain": True,
        "ingredients": [{"rawLine": text.strip()} for text, _ in ingredients],
        "steps": [{"order": i + 1, "instruction": text.strip()} for i, (text, _) in enumerate(steps)],
    }
    if translate and source_name:
        record["sourceName"] = source_name.strip()
    if chunk.split:
        record["split"] = True
    return record, None


LEADER_ROW = re.compile(r"^(?P<name>[^\d½¼¾⅓⅔⅛⅜⅝⅞.]*[^\W\d_][^\d½¼¾⅓⅔⅛⅜⅝⅞.]*?)\s*,?\s*(?:[.·…_]\s*){2,}(?P<amount>\S.*)$")
AMOUNT_TEXT = re.compile(rf"^(?:[\d{VULGAR}]|(?:a|an|one|two|three|four|five|six|seven|eight|nine|ten|twelve|half)\b)", re.I)


def leader_row(line: str) -> str | None:
    """A row of an ingredient table set with dot leaders ("Corn meal . . . . . . 1 pint"), as an ingredient line with
    its amount first ("1 pint Corn meal"), or None."""
    m = LEADER_ROW.match(line.strip())
    if not m:
        return None
    name, amount = m.group("name").strip(" ,"), m.group("amount").strip()
    if not name or not AMOUNT_TEXT.match(amount):
        return None
    return f"{amount.rstrip('.')} {name}"


def list_line(line: str) -> str | None:
    """An ingredient line as the book lists it, or None: a short line that starts with an amount and holds no sentence,
    or a dot-leader row."""
    text = line.strip()
    row = leader_row(text)
    if row:
        return row
    if len(text) <= 60 and LIST_START.match(text) and not SENTENCE_END.search(text):
        return re.sub(r"(?<![A-Z])\.$", "", text)
    return None


def join_lines(lines: list[str], words: frozenset[str] | set[str] | None) -> str:
    """Lines of prose joined into one paragraph; a word broken at a line end is joined, without its hyphen when the
    joined word is a known word ("beat-" / "en" is "beaten"), with it otherwise ("well-" / "beaten")."""
    out = ""
    for line in (line.strip() for line in lines):
        if not line:
            continue
        broken = re.search(r"([^\W\d_]+)-$", out)
        start = re.match(r"[^\W\d_]+", line)
        if broken and start and line[0].islower():
            joined = (broken.group(1) + start.group(0)).lower()
            whole = joined in words or (joined.endswith("s") and joined[:-1] in words) if words is not None else False
            out = out[:-1] + line if whole else out + line
        else:
            out = f"{out} {line}" if out else line
    return out


def unreadable_block(text: str, words: frozenset[str] | set[str]) -> bool:
    """Whether a recipe's text is too damaged to read: three or more of its short lines, and three in ten or more of
    them, fail the readability test (an OCR'd ingredient table, staging checkpoint 7, S22)."""
    short = [line for line in text.split("\n") if line.strip() and len(line.strip()) <= 60 and re.search(r"[^\W\d_]", line)]
    bad = sum(1 for line in short if unreadable(line, words))
    return bad >= 3 and bad * 10 >= len(short) * 3


def index_block(text: str) -> bool:
    """Whether a block is an index or a table of contents: five or more dot-leader rows, nearly all ending in a bare
    page number, and no sentence."""
    rows = [m for m in (LEADER_ROW.match(line.strip()) for line in text.split("\n")) if m]
    numbered = sum(1 for m in rows if re.fullmatch(r"\d{1,4}\.?", m.group("amount").strip()))
    prose = any(len(line.strip()) > 60 and SENTENCE_END.search(line) for line in text.split("\n"))
    return numbered >= 5 and numbered * 10 >= len(rows) * 9 and not prose


@dataclass
class Block:
    start: int
    end: int
    text: str
    title: str | None
    outcome: str
    record: dict | None = None
    reason: str = ""


def rule_record(text: str, title: str, words: frozenset[str] | set[str] | None) -> tuple[dict | None, str]:
    """A recipe read by rule from the regular shape of old cookbooks: its title line, then its ingredient list (lines
    that start with an amount, or dot-leader rows; a line in lower case or a bracket continues the line before), then
    its method in prose, one step per paragraph. None with the reason when the recipe is not in that shape: no list or
    no method, a list inside the method, or a method with no sentence."""
    lines = text.split("\n")
    at = next((i for i, line in enumerate(lines) if line.strip()), None)
    if at is None:
        return None, "empty"
    ingredients: list[str] = []
    method: list[str] = []
    in_method = False
    run = 0
    for line in lines[at + 1:]:
        stripped = line.strip()
        if not in_method:
            if not stripped:
                continue
            item = list_line(stripped)
            if item:
                ingredients.append(item)
                continue
            if ingredients and len(stripped) <= 60 and (stripped[0].islower() or stripped[0] == "("):
                ingredients[-1] = f"{ingredients[-1]} {stripped}"
                continue
            in_method = True
        if stripped and (leader_row(stripped) or (len(stripped) <= 40 and LIST_START.match(stripped) and not SENTENCE_END.search(stripped))):
            run += 1
            if run >= 2 or leader_row(stripped):
                return None, "a list inside the method"
        elif stripped:
            run = 0
        method.append(line)
    if len(ingredients) < 2:
        return None, "no ingredient list of two or more lines"
    paragraphs = [p for p in re.split(r"\n\s*\n", "\n".join(method)) if p.strip()]
    steps = [join_lines(p.split("\n"), words) for p in paragraphs]
    if not steps or not any(len(re.findall(r"[^\W\d_]", s)) >= 20 and re.search(r"[.!?;]", s) for s in steps):
        return None, "no method in prose"
    name = re.sub(r"\s*[.:]$", "", title.strip())
    return {"name": name, "ingredients": ingredients, "steps": steps}, "rule"


def blocks(text: str, words: frozenset[str] | set[str] | None = None) -> list[tuple[int, int, str | None]]:
    """The text cut at its recipe titles: each block's start, end and title (None for the text before the first title).
    With a word list, a line that only looks like a title and is not readable (an OCR'd table row) is no title."""
    titled = set(title_offsets(text))
    if words is not None:
        titled = {at for at in titled if not unreadable(text[at:].split("\n", 1)[0], words)}
    starts = sorted(titled | {0})
    out = []
    for start, end in zip(starts, starts[1:] + [len(text)]):
        if not text[start:end].strip():
            continue
        title = text[start:end].strip().split("\n", 1)[0].strip() if start in titled else None
        out.append((start, end, title))
    return out


def read_by_rule(text: str, words: frozenset[str] | set[str]) -> list[Block]:
    """Each recipe of a book read by rule where its shape allows (outcome "rule", with its checked record), marked
    "unreadable" where its text fails the readability test (no model is asked to read it; its pages want the page image)
    or holds the unread mark (UNREAD: a page image read where an amount was not read with confidence),
    "model" where only the model can split it (prose recipes, mixed shapes), and "none" where it holds no recipe at all
    (no line that starts with an amount, no measure named)."""
    out = []
    for start, end, title in blocks(text, words):
        piece = text[start:end]
        if index_block(piece):
            out.append(Block(start, end, piece, title, "none", reason="no recipe: an index or a table of contents"))
            continue
        if UNREAD in piece:
            out.append(Block(start, end, piece, title, "unreadable", reason="unreadable text: the page image was read, but not every "
                                                                            "amount with confidence"))
            continue
        if unreadable_block(piece, words):
            out.append(Block(start, end, piece, title, "unreadable", reason="unreadable text: the page image is needed"))
            continue
        if title is not None:
            recipe, why = rule_record(piece, title, words)
            if recipe is not None:
                record, problem = check_record(recipe, Chunk(start, end, piece), translate=False, words=words)
                if record is not None:
                    out.append(Block(start, end, piece, title, "rule", record=record, reason="rule"))
                    continue
                why = problem or why
        else:
            why = "no title"
        has_list = any(list_line(line) for line in piece.split("\n"))
        if not has_list and not units(piece):
            out.append(Block(start, end, piece, title, "none", reason="no recipe: no amount and no measure"))
            continue
        out.append(Block(start, end, piece, title, "model", reason=why))
    return out


def model_chunks(found: list[Block], limit: int = DEFAULT_LIMIT, hard: int = HARD_LIMIT) -> list[Chunk]:
    """The chunks the model is asked: runs of neighbouring blocks only the model can read, at most `limit` characters
    each unless one recipe is longer, a recipe longer than `hard` cut at paragraph ends (rule 2)."""
    out: list[Chunk] = []
    current: list[Block] = []

    def flush():
        if current:
            s, e = current[0].start, current[-1].end
            out.append(Chunk(s, e, "".join(b.text for b in current)))
            current.clear()
    for block in found:
        if block.outcome != "model":
            flush()
            continue
        if block.end - block.start > hard:
            flush()
            whole = block.text
            out.extend(Chunk(block.start + c.start, block.start + c.end, c.text, split=True) for c in _split(whole, 0, len(whole), hard))
            continue
        if current and block.end - current[0].start > limit:
            flush()
        current.append(block)
    flush()
    return out


def answer_key(model: str, system: str, user: str) -> str:
    """The key of a model answer in a checkpoint file (rule 6)."""
    return hashlib.sha256((model + "\0" + system + "\0" + user).encode("utf-8")).hexdigest()


def answered_keys(checkpoint: str) -> set[str]:
    """The keys of the readable answers a checkpoint file holds."""
    keys: set[str] = set()
    if checkpoint and os.path.exists(checkpoint):
        with open(checkpoint, encoding="utf-8") as handle:
            for text in handle:
                try:
                    row = json.loads(text)
                except json.JSONDecodeError:
                    continue
                if isinstance(row, dict) and isinstance(row.get("key"), str) and isinstance(row.get("answer"), str) and readable(row["answer"]):
                    keys.add(row["key"])
    return keys


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
        except ModelCallFailed as exc:
            result.failed_chunks += 1
            log(f"chunk at {chunk.start}: the model did not answer ({exc}); its recipes wait for the next run")
            continue
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
    """Names the recipe each record replaces, by the title it quotes, else by its name; returns how many were pinned.
    A record whose title and name point to two recipes, and every record of a recipe two records point to, stays unpinned."""
    records = list(records)
    chosen: dict[int, int] = {}
    for index, record in enumerate(records):
        found = {pins[normalize(k)] for k in (record.get("sourceName"), record.get("name")) if k and normalize(k) in pins}
        if len(found) == 1:
            chosen[index] = found.pop()
        elif found:
            log(f"{record.get('name')}: its title and name point to recipes {sorted(found)}; left unpinned")
    counts = Counter(chosen.values())
    count = 0
    for index, recipe_id in chosen.items():
        if counts[recipe_id] > 1:
            log(f"{records[index].get('name')}: recipe {recipe_id} is named by {counts[recipe_id]} records; left unpinned")
            continue
        records[index]["replacesRecipeId"] = recipe_id
        count += 1
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
    parser.add_argument("--timeout", type=int, default=600, help="seconds one model call may take (default 600)")
    parser.add_argument("--retries", type=int, default=2, help="times a failed or timed-out call is asked again (default 2)")
    parser.add_argument("--checkpoint", help="JSONL of the model's answers, kept as they arrive and read back on the next run (default: <out>.answers.jsonl)")
    args = parser.parse_args(argv)

    with open(args.book, encoding="utf-8", errors="replace") as handle:
        text = handle.read().replace("\r\n", "\n")
    ask = resumable(ollama(args.host, args.model, timeout=args.timeout), args.checkpoint or args.out + ".answers.jsonl", retries=args.retries, model=args.model)
    result = extract(text, ask, translate=args.translate, limit=args.limit, hard=args.hard_limit)
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

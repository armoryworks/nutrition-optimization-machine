"""
Scoring and decision core for the recipe image finder.

Everything here is pure: no network, no model calls. The finder gathers evidence
(source metadata, a blind caption, directed visual answers, a second model's
scores) and this module turns that evidence into one auditable decision per
recipe:

  * attach  — the chosen image passed EVERY check (metadata, blind caption,
              directed VQA, independent ranking). Safe to publish unattended.
  * review  — the best image passed the content gate and enough checks to be
              worth a human's time, but not all of them.
  * none    — nothing worth showing anyone.

The four checks are deliberately independent of each other:

  (a) metadata  what the uploader said the picture is (title, description, tags);
  (b) caption   what a vision model says it sees WITHOUT being told the recipe;
  (c) vqa       directed questions: is this a finished dish, is it plausibly this
                dish, which ingredients are visible;
  (d) ranking   a different vision model family scores every candidate and compares
                them head-to-head in both orders; the chosen image must win every
                comparison (its clear favourite, not a coin toss).

A wrong photo on a published recipe is worse than no photo, so a single failed
check is enough to keep an image off the page without a human looking at it.
"""

from __future__ import annotations

import math
import re
from dataclasses import dataclass, field

NAME_NOISE = {
    "a", "an", "and", "the", "with", "of", "in", "on", "for", "to", "from", "my", "our",
    "your", "easy", "easiest", "best", "classic", "simple", "quick", "homemade", "home",
    "made", "perfect", "ultimate", "favorite", "favourite", "recipe", "style", "healthy",
    "delicious", "amazing", "authentic", "traditional", "famous", "copycat", "minute",
    "minutes", "ingredient", "ingredients", "one", "pot", "pan", "sheet", "skillet",
    "instant", "slow", "cooker", "crockpot", "air", "fryer", "baked",
    "vegan", "keto", "grandma", "grandmas",
    "mom", "moms", "weeknight", "fluffy", "creamy", "crispy", "spicy", "super", "really",
    "or", "x", "s",
}

INGREDIENT_NOISE = {
    "fresh", "freshly", "ground", "chopped", "diced", "minced", "sliced", "large", "small",
    "medium", "whole", "unsalted", "salted", "granulated", "all", "purpose", "allpurpose",
    "extra", "virgin", "light", "dark", "boneless", "skinless", "raw", "cooked", "dried",
    "frozen", "canned", "low", "sodium", "reduced", "plain", "pure", "organic", "finely",
    "roughly", "melted", "softened", "room", "temperature", "optional", "to", "taste",
    "of", "and", "or", "for", "the", "a", "cup", "cups", "tbsp", "tsp", "tablespoon",
    "teaspoon", "pinch", "dash", "clove", "cloves", "package", "can", "jar", "bunch",
    "piece", "pieces", "kosher", "sea", "table", "black", "white", "powder", "baking",
    "vanilla", "extract", "oil", "water", "salt", "pepper", "sugar", "flour", "spray",
    "leave", "leaf", "sprig", "stalk", "head", "inch", "ounce", "oz", "lb", "pound", "gram",
    "heavy", "thick", "thin", "crushed", "grated", "shredded", "juice", "zest", "divided",
}

STAPLE_HEADS = {"oil", "salt", "pepper", "sugar", "flour", "water", "spray", "powder", "extract",
                "soda", "starch", "yeast", "stock", "broth"}

WATERMARK_OR_JUNK = (
    "watermark", "logo", "text overlay", "collage", "packaging", "package", "label", "menu",
    "screenshot", "advertisement", "illustration", "drawing", "cartoon", "diagram",
)

_TOKEN_RE = re.compile(r"[a-z]+")


@dataclass
class Thresholds:
    """Every cut-off the decision uses. All configurable from the CLI."""

    name_coverage: float = 0.6
    metadata_embedding: float = 0.62
    caption_similarity: float = 0.64
    vqa_ingredient_overlap: float = 0.5
    rank_min_score: float = 7.0
    rank_win_share: float = 1.0
    rank_min_rivals: int = 1
    review_min_checks: int = 2


@dataclass
class RecipeText:
    recipe_id: int
    name: str
    description: str = ""
    dish_group: str = ""
    ingredients: list[str] = field(default_factory=list)
    steps: list[str] = field(default_factory=list)


@dataclass
class CheckResult:
    passed: bool
    score: float
    reasons: list[str] = field(default_factory=list)
    detail: dict = field(default_factory=dict)

    def as_dict(self) -> dict:
        return {"passed": self.passed, "score": round(self.score, 4),
                "reasons": self.reasons, **self.detail}


@dataclass
class CandidateEvidence:
    key: str
    metadata: CheckResult | None = None
    caption: CheckResult | None = None
    vqa: CheckResult | None = None
    content_ok: bool = True
    content_reasons: list[str] = field(default_factory=list)
    rank_score: float | None = None
    pairwise: dict[str, list[bool]] = field(default_factory=dict)
    duplicate_of: str | None = None

    def checks(self) -> dict[str, CheckResult | None]:
        return {"metadata": self.metadata, "caption": self.caption, "vqa": self.vqa}


@dataclass
class Decision:
    outcome: str
    chosen: str | None
    review: list[str]
    reasons: list[str]
    per_candidate: dict[str, dict]


def stem(token: str) -> str:
    """Light, symmetric plural folding: cookies/cookie -> cooki, berries/berry -> berri, tomatoes -> tomato."""
    if len(token) > 4 and token.endswith("ies"):
        return token[:-3] + "i"
    if len(token) > 4 and token.endswith(("oes", "ches", "shes", "sses", "xes")):
        token = token[:-2]
    elif len(token) > 3 and token.endswith("s") and not token.endswith(("ss", "us", "is")):
        token = token[:-1]
    if len(token) > 3 and token.endswith("ie"):
        return token[:-1]
    if len(token) > 3 and token.endswith("y"):
        return token[:-1] + "i"
    return token


def words(text: str) -> list[str]:
    return _TOKEN_RE.findall((text or "").lower().replace("'", ""))


def tokens(text: str) -> list[str]:
    return [stem(t) for t in words(text)]


_NAME_NOISE_STEMS = {stem(n) for n in NAME_NOISE} | NAME_NOISE
_INGREDIENT_NOISE_STEMS = {stem(n) for n in INGREDIENT_NOISE} | INGREDIENT_NOISE
_STAPLE_STEMS = {stem(n) for n in STAPLE_HEADS}


def _name_words(name: str) -> list[str]:
    raw = words(name)
    dropped = {i for i, w in enumerate(raw) if w == "free"} | {i - 1 for i, w in enumerate(raw) if w == "free"}
    dropped |= {i for i, w in enumerate(raw) if w == "low"} | {i + 1 for i, w in enumerate(raw) if w == "low"}
    kept: list[str] = []
    for index, word in enumerate(raw):
        if index in dropped or len(word) < 3 or word in _NAME_NOISE_STEMS or stem(word) in _NAME_NOISE_STEMS:
            continue
        if stem(word) not in {stem(k) for k in kept}:
            kept.append(word)
    return kept


def name_key_tokens(name: str) -> list[str]:
    """The words that identify the dish (stemmed), with marketing and method noise removed."""
    return [stem(w) for w in _name_words(name)]


def _ingredient_head_words(ingredients: list[str]) -> list[str]:
    heads: list[str] = []
    for ingredient in ingredients:
        raw = [w for w in words(ingredient.split(",")[0]) if len(w) > 1]
        if not raw or stem(raw[-1]) in _STAPLE_STEMS:
            continue
        meaningful = [w for w in raw if len(w) > 2 and w not in _INGREDIENT_NOISE_STEMS
                      and stem(w) not in _INGREDIENT_NOISE_STEMS]
        if meaningful and stem(meaningful[-1]) not in {stem(h) for h in heads}:
            heads.append(meaningful[-1])
    return heads


def ingredient_heads(ingredients: list[str]) -> list[str]:
    """
    One identifying word per ingredient, stemmed (the last meaningful one: "boneless
    chicken thighs" -> "thigh"). Pantry staples (oil, salt, flour, ...) are dropped
    because every dish contains them and they prove nothing about a photo.
    """
    return [stem(h) for h in _ingredient_head_words(ingredients)]


def clean_dish_name(name: str) -> str:
    return " ".join(_name_words(name))


def build_queries(recipe: RecipeText, limit: int = 3) -> list[str]:
    """Search phrases, most specific first: the dish name, the dish group, name + main ingredient."""
    queries: list[str] = []
    cleaned = clean_dish_name(recipe.name)
    if cleaned:
        queries.append(cleaned)
    group = clean_dish_name(recipe.dish_group)
    if group and group != cleaned:
        queries.append(group)
    name_stems = set(name_key_tokens(recipe.name))
    main = next((h for h in _ingredient_head_words(recipe.ingredients) if stem(h) not in name_stems), None)
    if cleaned and main and len(cleaned.split()) < 3:
        queries.append(f"{cleaned} {main}")
    return list(dict.fromkeys(q for q in queries if q))[:limit]


def name_coverage(name: str, candidate_text: str) -> float:
    key = name_key_tokens(name)
    if not key:
        return 0.0
    have = set(tokens(candidate_text))
    hits = sum(1 for k in key if k in have or (len(k) >= 4 and any(len(t) > len(k) + 2 and k in t for t in have)))
    return hits / len(key)


def ingredient_mentions(heads: list[str], text: str) -> list[str]:
    have = set(tokens(text))
    return [h for h in heads if h in have]


def cosine(a: list[float] | None, b: list[float] | None) -> float:
    if not a or not b or len(a) != len(b):
        return 0.0
    dot = sum(x * y for x, y in zip(a, b))
    na = math.sqrt(sum(x * x for x in a))
    nb = math.sqrt(sum(y * y for y in b))
    return dot / (na * nb) if na and nb else 0.0


def recipe_document(recipe: RecipeText, max_steps: int = 3) -> str:
    parts = [recipe.name]
    if recipe.description:
        parts.append(recipe.description)
    if recipe.ingredients:
        parts.append("Ingredients: " + ", ".join(recipe.ingredients[:12]))
    if recipe.steps:
        parts.append(" ".join(recipe.steps[-max_steps:]))
    return ". ".join(p.strip().rstrip(".") for p in parts if p.strip())[:2000]


def metadata_check(recipe: RecipeText, candidate_text: str, embedding_similarity: float | None,
                   thresholds: Thresholds) -> CheckResult:
    """(a) Does what the uploader wrote about the picture describe this dish?"""
    coverage = name_coverage(recipe.name, candidate_text)
    mentions = ingredient_mentions(ingredient_heads(recipe.ingredients), candidate_text)
    sim = embedding_similarity or 0.0
    reasons: list[str] = []
    lexical_ok = coverage >= thresholds.name_coverage
    semantic_ok = sim >= thresholds.metadata_embedding
    if not lexical_ok:
        reasons.append(f"title/tags cover {coverage:.0%} of the dish name "
                       f"(need {thresholds.name_coverage:.0%})")
    if not semantic_ok:
        reasons.append(f"metadata embedding similarity {sim:.3f} < {thresholds.metadata_embedding}")
    passed = lexical_ok and semantic_ok
    score = 0.6 * coverage + 0.4 * sim
    return CheckResult(passed, score, reasons, {
        "name_coverage": round(coverage, 3), "embedding_similarity": round(sim, 4),
        "ingredient_mentions": mentions,
    })


def caption_check(caption: str, similarities: dict[str, float], thresholds: Thresholds) -> CheckResult:
    """
    (b) The blind caption, compared to the recipe. `similarities` holds the caption's
    embedding similarity to the recipe name, description and instructions; the best of
    them must clear the threshold, because a caption describes a plate, and only one of
    those texts may describe the plate.
    """
    best_field, best = max(similarities.items(), key=lambda kv: kv[1]) if similarities else ("", 0.0)
    reasons: list[str] = []
    passed = best >= thresholds.caption_similarity
    if not passed:
        reasons.append(f"blind caption is not about this recipe "
                       f"(best similarity {best:.3f} via {best_field or 'nothing'}, "
                       f"need {thresholds.caption_similarity})")
    lowered = caption.lower()
    junk = [w for w in WATERMARK_OR_JUNK if w in lowered]
    if junk:
        passed = False
        reasons.append("caption mentions " + ", ".join(junk))
    return CheckResult(passed, best, reasons, {
        "caption": caption, "similarities": {k: round(v, 4) for k, v in similarities.items()},
    })


def _yes(value) -> bool:
    if isinstance(value, bool):
        return value
    return str(value).strip().lower() in {"yes", "true", "1", "y"}


def content_gate(answers: dict) -> tuple[bool, list[str]]:
    """The part of (c) that no human review can fix: what kind of picture this is."""
    reasons: list[str] = []
    if not _yes(answers.get("is_photograph", True)):
        reasons.append("not a photograph")
    if not _yes(answers.get("finished_dish")):
        reasons.append("not a finished, prepared dish")
    for key, label in (("raw_ingredients_only", "raw ingredients only"),
                       ("packaging_or_product", "packaging or a retail product"),
                       ("people_prominent", "people are prominent"),
                       ("text_or_watermark", "visible text or watermark"),
                       ("collage", "a collage or multiple panels")):
        if _yes(answers.get(key, False)):
            reasons.append(label)
    return not reasons, reasons


def recipe_vocabulary(recipe: RecipeText) -> set[str]:
    """
    Foods the recipe itself mentions: ingredient heads, the dish name, and the meaningful
    words of the description and steps (so "serve with blueberries" counts as a garnish
    the recipe expects, while bacon on a plain-pancake photo does not).
    """
    vocabulary = set(ingredient_heads(recipe.ingredients)) | set(name_key_tokens(recipe.name))
    for text in [recipe.description, *recipe.steps]:
        vocabulary |= {t for t in tokens(text) if len(t) > 3 and t not in _INGREDIENT_NOISE_STEMS
                       and t not in _NAME_NOISE_STEMS}
    return vocabulary


def vqa_check(recipe: RecipeText, answers: dict, thresholds: Thresholds) -> tuple[CheckResult, bool, list[str]]:
    """
    (c) Directed questions. Returns the check plus the content gate separately: a
    content failure disqualifies an image outright, a dish/ingredient doubt only
    downgrades it to review.
    """
    content_ok, content_reasons = content_gate(answers)
    reasons = list(content_reasons)
    dish = str(answers.get("dish_matches", "unsure")).strip().lower()
    dish_ok = dish in {"yes", "true"}
    if not dish_ok:
        reasons.append(f"model says this is not plausibly '{recipe.name}' ({dish})")

    visible = [v for v in answers.get("visible_ingredients", []) or [] if isinstance(v, str)]
    known = recipe_vocabulary(recipe)
    matched = [v for v in visible if set(tokens(v)) & known]
    overlap = len(matched) / len(visible) if visible else 0.0
    overlap_ok = bool(matched) and overlap >= thresholds.vqa_ingredient_overlap
    if not overlap_ok:
        reasons.append(f"visible ingredients overlap {overlap:.0%} with the recipe "
                       f"(need {thresholds.vqa_ingredient_overlap:.0%}, at least one)")

    passed = content_ok and dish_ok and overlap_ok
    score = (0.5 if dish_ok else 0.0) + 0.5 * overlap
    return CheckResult(passed, score, reasons, {
        "dish_matches": dish, "visible_ingredients": visible, "matched_ingredients": matched,
        "ingredient_overlap": round(overlap, 3), "answers": answers,
    }), content_ok, content_reasons


def rivals_of(evidence: dict[str, CandidateEvidence], key: str) -> list[str]:
    """Candidates the chosen image must beat head-to-head: eligible, and not copies of it."""
    return [k for k in eligible(evidence) if k != key]


def eligible(evidence: dict[str, CandidateEvidence]) -> list[str]:
    return [k for k, e in evidence.items() if e.content_ok and e.duplicate_of is None]


def round_robin(evidence: dict[str, CandidateEvidence]) -> list[tuple[str, str]]:
    """Every pair of eligible candidates; each is shown to the second model in both orders."""
    keys = eligible(evidence)
    return [(a, b) for i, a in enumerate(keys) for b in keys[i + 1:]]


def record_vote(evidence: dict[str, CandidateEvidence], first: str, second: str, answer: str) -> None:
    """One head-to-head answer ("A", "B" or "same") with `first` shown as Image A. A tie is a win for neither."""
    choice = answer.strip().upper()
    evidence[first].pairwise.setdefault(second, []).append(choice == "A")
    evidence[second].pairwise.setdefault(first, []).append(choice == "B")


def rank_check(evidence: dict[str, CandidateEvidence], chosen: str, thresholds: Thresholds) -> CheckResult:
    """
    (d) A second, different model must independently back the chosen image: it scores the
    image on its own (floor `rank_min_score`), and in head-to-head comparisons against every
    rival, shown in BOTH orders to cancel position bias, it must win at least
    `rank_win_share` of the votes (default: all of them). Copies of the chosen photo are not
    rivals. Absolute scores alone are not enough — small models rate most food photos 9/10.
    """
    e = evidence[chosen]
    reasons: list[str] = []
    if e.rank_score is None:
        reasons.append("second model gave no score")
    elif e.rank_score < thresholds.rank_min_score:
        reasons.append(f"second model score {e.rank_score:g} < {thresholds.rank_min_score:g}")

    rivals = rivals_of(evidence, chosen)
    if len(rivals) < thresholds.rank_min_rivals:
        reasons.append(f"{len(rivals)} rival(s) to compare against (need {thresholds.rank_min_rivals}); "
                       "a lone candidate cannot show a margin")
    missing = [r for r in rivals if len(e.pairwise.get(r, [])) < 2]
    votes = [v for r in rivals for v in e.pairwise.get(r, [])]
    share = sum(votes) / len(votes) if votes else (1.0 if not rivals else 0.0)
    if missing:
        reasons.append(f"not compared head-to-head with {len(missing)} rival(s)")
    elif share < thresholds.rank_win_share:
        lost = [r for r in rivals if not all(e.pairwise[r])]
        reasons.append(f"won {share:.0%} of head-to-head votes (need {thresholds.rank_win_share:.0%}); "
                       f"not preferred over {', '.join(lost)}")
    return CheckResult(not reasons, share, reasons, {
        "score": e.rank_score, "rivals": len(rivals), "win_share": round(share, 3),
        "pairwise": {k: v for k, v in e.pairwise.items()},
    })


def combined_score(e: CandidateEvidence) -> float:
    parts = [(e.metadata, 0.2), (e.caption, 0.3), (e.vqa, 0.3)]
    total = sum(w * (c.score if c else 0.0) for c, w in parts)
    absolute = (e.rank_score or 0.0) / 10.0
    votes = [v for vs in e.pairwise.values() for v in vs]
    share = sum(votes) / len(votes) if votes else absolute
    return total + 0.1 * absolute + 0.1 * share


def decide(evidence: dict[str, CandidateEvidence], thresholds: Thresholds, max_review: int = 3) -> Decision:
    """Pick the best candidate and decide attach / review / none. Pure and deterministic."""
    per: dict[str, dict] = {}
    eligible = {k: e for k, e in evidence.items() if e.content_ok and e.duplicate_of is None}
    for key, e in evidence.items():
        per[key] = {name: (c.as_dict() if c else None) for name, c in e.checks().items()}
        per[key]["content_ok"] = e.content_ok
        per[key]["content_reasons"] = e.content_reasons
        per[key]["rank_score"] = e.rank_score
        per[key]["pairwise"] = e.pairwise
        per[key]["duplicate_of"] = e.duplicate_of
        per[key]["combined"] = round(combined_score(e), 4)

    if not eligible:
        return Decision("none", None, [], ["no candidate passed the content gate"], per)

    ordered = sorted(eligible, key=lambda k: (combined_score(eligible[k]), k), reverse=True)
    passes: dict[str, dict[str, bool]] = {}
    for key in ordered:
        e = evidence[key]
        rank = rank_check(evidence, key, thresholds)
        per[key]["ranking"] = rank.as_dict()
        passes[key] = {
            "metadata": bool(e.metadata and e.metadata.passed),
            "caption": bool(e.caption and e.caption.passed),
            "vqa": bool(e.vqa and e.vqa.passed),
            "ranking": rank.passed,
        }
        per[key]["checks_passed"] = sum(passes[key].values())

    full = [k for k in ordered if all(passes[k].values())]
    if full:
        chosen = full[0]
        return Decision("attach", chosen, [], [f"{chosen} passed all four checks"], per)

    reviewable = [k for k in ordered if sum(passes[k].values()) >= thresholds.review_min_checks]
    if reviewable:
        failed = [name for name, ok in passes[reviewable[0]].items() if not ok]
        return Decision("review", reviewable[0], reviewable[:max_review],
                        [f"best candidate {reviewable[0]} failed: {', '.join(failed)}"], per)
    return Decision("none", None, [], [f"no candidate passed {thresholds.review_min_checks}+ checks"], per)


def dhash_distance(a: int | None, b: int | None) -> int:
    if a is None or b is None:
        return 64
    return bin(a ^ b).count("1")


def mark_duplicates(hashes: dict[str, int | None], preference: list[str], max_distance: int = 6) -> dict[str, str]:
    """
    Near-identical pictures (the same Flickr photo on Commons and via Openverse) are
    collapsed to the first key in `preference`, so one photo cannot win the ranking
    margin against itself.
    """
    duplicates: dict[str, str] = {}
    kept: list[str] = []
    for key in preference:
        original = next((k for k in kept if dhash_distance(hashes.get(key), hashes.get(k)) <= max_distance), None)
        if original:
            duplicates[key] = original
        else:
            kept.append(key)
    return duplicates

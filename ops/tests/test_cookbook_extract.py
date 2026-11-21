"""
Tests for the cookbook re-extraction: chunks cut at recipe titles, the rule-given fidelity check,
the Artusi number check on a hand-checked fixture, and pinning records to the recipes they replace.
No network: the model is a recorded stand-in.
Run: python3 -m unittest discover -s ops/tests
"""

import importlib.util
import json
import os
import sys
import tempfile
import unittest

OPS = os.path.join(os.path.dirname(__file__), "..")
_spec = importlib.util.spec_from_file_location("cookbook_extract", os.path.join(OPS, "cookbook-extract.py"))
extract = importlib.util.module_from_spec(_spec)
sys.modules["cookbook_extract"] = extract
_spec.loader.exec_module(extract)

with open(os.path.join(os.path.dirname(__file__), "fixtures", "artusi-sample.json"), encoding="utf-8") as _handle:
    ARTUSI = json.load(_handle)["recipes"]

BOOK = """THE HOME COOK BOOK

PREFACE

These receipts were gathered from the ladies of the town.

GINGERBREAD

1 cup molasses
½ cup butter
2 teaspoonfuls ginger
3 cups flour

Mix the molasses and butter; add the ginger and flour. Bake in a shallow pan.

Corn Muffins

2 cups corn meal
1 cup flour
2 eggs

Beat the eggs, add the meal and flour, and bake in muffin tins.

Apple Snow

6 apples
Whites of 3 eggs
1 cup sugar

Stew the apples, beat the whites stiff, and fold in the sugar.
"""


def artusi_text(recipes):
    return "\n\n".join(r["title"] + "\n\n" + "\n".join(r["italian"]) + "\n\nSi mescola tutto e si cuoce." for r in recipes) + "\n"


class ChunkTests(unittest.TestCase):
    def test_titles_are_found_by_their_shape(self):
        lines = BOOK.split("\n")
        titles = [lines[i] for i in range(len(lines)) if extract.is_title(lines, i)]
        self.assertIn("GINGERBREAD", titles)
        self.assertIn("Corn Muffins", titles)
        self.assertIn("Apple Snow", titles)
        self.assertNotIn("1 cup molasses", titles)
        self.assertNotIn("2 cups corn meal", titles)

    def test_a_chunk_never_cuts_a_recipe_and_starts_at_a_title(self):
        pieces = extract.chunks(BOOK, limit=200)
        joined = "".join(p.text for p in pieces)
        self.assertEqual(joined, BOOK[pieces[0].start:])
        for title in ("GINGERBREAD", "Corn Muffins", "Apple Snow"):
            holding = [p for p in pieces if title in p.text]
            self.assertEqual(len(holding), 1)
            self.assertTrue(holding[0].text.lstrip().startswith(title) or holding[0].start == 0)
        gingerbread = next(p for p in pieces if "GINGERBREAD" in p.text)
        self.assertIn("3 cups flour", gingerbread.text)
        self.assertIn("shallow pan", gingerbread.text)

    def test_recipes_share_a_chunk_up_to_the_limit(self):
        self.assertEqual(len(extract.chunks(BOOK, limit=4000)), 1)
        self.assertGreater(len(extract.chunks(BOOK, limit=150)), 2)

    def test_numbered_artusi_titles_are_boundaries(self):
        text = artusi_text(ARTUSI[:5])
        pieces = extract.chunks(text, limit=120)
        self.assertEqual(len(pieces), 5)
        self.assertTrue(all(p.text.startswith(r["title"]) for p, r in zip(pieces, ARTUSI[:5])))

    def test_one_recipe_longer_than_the_hard_limit_is_cut_at_paragraphs_and_marked(self):
        long = "LONG PUDDING\n\n" + "\n\n".join("Stir the pudding well and let it rest. " * 4 for _ in range(30)) + "\n"
        pieces = extract.chunks(long, limit=200, hard=600)
        self.assertGreater(len(pieces), 1)
        self.assertTrue(all(p.split for p in pieces))
        self.assertTrue(all(len(p.text) <= 600 for p in pieces[:-1]))


class FidelityTests(unittest.TestCase):
    def test_a_line_as_printed_passes_after_normalization(self):
        self.assertIsNone(extract.fidelity("½ cup butter", BOOK))
        self.assertIsNone(extract.fidelity("1  Cup  Molasses.", BOOK))
        self.assertIsNone(extract.fidelity("2 teaspoonfuls &amp; ginger".replace(" &amp;", ""), BOOK))

    def test_a_line_whose_numbers_and_words_are_in_the_book_passes(self):
        self.assertIsNone(extract.fidelity("3 cups flour, sifted".replace(", sifted", ""), BOOK))
        self.assertIsNone(extract.fidelity("whites of 3 eggs", BOOK))

    def test_a_changed_number_or_an_invented_word_fails(self):
        self.assertIn("number", extract.fidelity("4 cups flour", BOOK))
        self.assertIn("words", extract.fidelity("1 cup brown sugar", BOOK))
        self.assertEqual(extract.fidelity("", BOOK), "empty line")


class NumberCheckTests(unittest.TestCase):
    def test_every_hand_checked_artusi_line_passes(self):
        self.assertEqual(len(ARTUSI), 20)
        for recipe in ARTUSI:
            self.assertEqual(len(recipe["italian"]), len(recipe["english"]), recipe["title"])
            for italian, english in zip(recipe["italian"], recipe["english"]):
                self.assertIsNone(extract.number_check(english, italian), f"{recipe['title']}: {english} / {italian}")

    def test_a_changed_dropped_or_added_number_or_unit_fails(self):
        self.assertIn("numbers", extract.number_check("300 grams of flour", "Farina, grammi 250."))
        self.assertIn("numbers", extract.number_check("flour", "Farina, grammi 250."))
        self.assertIn("numbers", extract.number_check("2 eggs and 50 grams of butter", "Uova, N. 2."))
        self.assertIn("units", extract.number_check("250 ounces of flour", "Farina, grammi 250."))
        self.assertIn("units", extract.number_check("2 tablespoons of milk", "Latte, decilitri 2."))
        self.assertIn("numbers", extract.number_check("a litre of milk", "Latte, mezzo litro."))

    def test_italian_and_english_units_read_as_one(self):
        self.assertEqual(set(extract.units("Farina, grammi 250, latte decilitri 2, olio cucchiai 3, sale cucchiaino 1")), {"g", "dl", "tbsp", "tsp"})
        self.assertEqual(set(extract.units("250 grams of flour, 2 decilitres of milk, 3 tablespoons of oil, 1 teaspoon of salt")), {"g", "dl", "tbsp", "tsp"})
        self.assertEqual(set(extract.units("un pollo di chilogrammi 1")), {"kg"})


def stand_in(answers):
    calls = []

    def ask(system, user):
        calls.append((system, user))
        return answers(user) if callable(answers) else answers.pop(0)

    ask.calls = calls
    return ask


class ExtractTests(unittest.TestCase):
    def test_kept_records_are_scraper_records_and_unfaithful_ones_are_dropped_with_their_reason(self):
        def answers(chunk):
            return json.dumps({"recipes": [
                {"name": "Gingerbread", "ingredients": ["1 cup molasses", "½ cup butter", "2 teaspoonfuls ginger", "3 cups flour"],
                 "steps": ["Mix the molasses and butter; add the ginger and flour.", "Bake in a shallow pan."]},
                {"name": "Corn Muffins", "ingredients": ["2 cups corn meal", "1 cup flour", "5 eggs"], "steps": ["Beat the eggs."]},
                {"name": "Apple Snow", "ingredients": [], "steps": []},
            ]})

        result = extract.extract(BOOK, stand_in(answers))

        self.assertEqual([r["name"] for r in result.kept], ["Gingerbread"])
        record = result.kept[0]
        self.assertEqual(record["ingredients"][1], {"rawLine": "½ cup butter"})
        self.assertEqual(record["steps"][1], {"order": 2, "instruction": "Bake in a shallow pan."})
        self.assertTrue(record["publicDomain"])
        reasons = {r["name"]: r["reason"] for r in result.rejects}
        self.assertTrue(reasons["Corn Muffins"].startswith("fidelity: 5 eggs"))
        self.assertEqual(reasons["Apple Snow"], "no name or no ingredient line")
        self.assertEqual(result.summary()["droppedByReason"], {"fidelity": 1, "no name or no ingredient line": 1})

    def test_an_unreadable_answer_drops_nothing_and_is_counted(self):
        result = extract.extract(BOOK, stand_in(["not json"]))
        self.assertEqual((result.unreadable_chunks, len(result.kept), len(result.rejects)), (1, 0, 0))

    def test_a_translation_keeps_only_records_whose_every_line_carries_its_italian_numbers(self):
        recipes = ARTUSI[:3]
        text = artusi_text(recipes)

        def answers(chunk):
            out = []
            for r in recipes:
                if r["title"] not in chunk:
                    continue
                english = list(r["english"])
                if r["title"].startswith("2."):
                    english[0] = "600 grams of lean beef"
                out.append({"sourceName": r["title"], "name": r["name"],
                            "ingredients": [{"text": e, "source": i} for e, i in zip(english, r["italian"])],
                            "steps": [{"text": "Mix everything and cook it.", "source": "Si mescola tutto e si cuoce."}]})
            return json.dumps({"recipes": out})

        result = extract.extract(text, stand_in(answers), translate=True)

        self.assertEqual([r["name"] for r in result.kept], ["Shortcrust pastry", "Risotto Milanese style"])
        self.assertEqual(result.kept[0]["sourceName"], "1. Pasta frolla")
        self.assertEqual(len(result.rejects), 1)
        self.assertTrue(result.rejects[0]["reason"].startswith("number check: 600 grams of lean beef"))

    def test_a_translation_quoting_a_line_not_in_the_book_or_merging_two_lines_is_dropped(self):
        recipe = ARTUSI[0]
        text = artusi_text([recipe])
        invented = {"name": "Shortcrust pastry", "ingredients": [{"text": "250 grams of flour", "source": "Farina bianca, grammi 250."}], "steps": []}
        merged = {"name": "Shortcrust pastry", "ingredients": [{"text": "250 grams of flour", "source": "Farina, grammi 250."},
                                                               {"text": "flour", "source": "Farina, grammi 250."}], "steps": []}
        unquoted = {"name": "Shortcrust pastry", "ingredients": [{"text": "250 grams of flour"}], "steps": []}

        result = extract.extract(text, stand_in([json.dumps({"recipes": [invented, merged, unquoted]})]), translate=True)

        self.assertEqual(result.kept, [])
        self.assertEqual([r["reason"].split(":")[0] for r in result.rejects],
                         ["an Italian line not in the book", "two English lines translate one Italian line", "an ingredient line quotes no Italian line"])


class PinTests(unittest.TestCase):
    def test_records_are_pinned_from_the_first_attach_log_by_name_or_quoted_title_and_the_csv_wins(self):
        with tempfile.TemporaryDirectory() as folder:
            log_path = os.path.join(folder, "attach.jsonl")
            with open(log_path, "w", encoding="utf-8") as handle:
                for entry in (
                    {"file": "cookbook-pg1.jsonl", "line": 1, "name": "Gingerbread", "outcome": "matched", "recipeId": 10},
                    {"file": "cookbook-pg1.jsonl", "line": 2, "name": "1. Pasta frolla", "outcome": "already-attached", "recipeId": 17338},
                    {"file": "cookbook-pg1.jsonl", "line": 3, "name": "Twice", "outcome": "matched", "recipeId": 1},
                    {"file": "cookbook-pg1.jsonl", "line": 4, "name": "Twice", "outcome": "matched", "recipeId": 2},
                    {"file": "cookbook-pg1.jsonl", "line": 5, "name": "Lost", "outcome": "low-score", "recipeId": 3},
                    {"file": "cookbook-pg2.jsonl", "line": 1, "name": "Corn Muffins", "outcome": "matched", "recipeId": 99},
                ):
                    handle.write(json.dumps(entry) + "\n")
            csv_path = os.path.join(folder, "pins.csv")
            with open(csv_path, "w", encoding="utf-8") as handle:
                handle.write("name,recipeId\nCorn Muffins,42\n")

            pins = extract.load_pins(log_path, "cookbook-pg1.jsonl", csv_path)
            records = [{"name": "Gingerbread"}, {"name": "Shortcrust pastry", "sourceName": "1. Pasta frolla"}, {"name": "Twice"},
                       {"name": "Lost"}, {"name": "Corn Muffins"}]
            count = extract.pin(records, pins)

        self.assertEqual(count, 3)
        self.assertEqual([r.get("replacesRecipeId") for r in records], [10, 17338, None, None, 42])


class MainTests(unittest.TestCase):
    def test_main_reads_the_book_writes_kept_and_dropped_records_and_asks_only_the_named_host(self):
        asked = []

        def fake_ollama(host, model, timeout=600):
            asked.append((host, model))
            return stand_in(lambda chunk: json.dumps({"recipes": [{"name": "Gingerbread", "ingredients": ["1 cup molasses", "½ cup butter", "2 teaspoonfuls ginger", "3 cups flour"], "steps": ["Bake."]},
                                                                   {"name": "Muffins", "ingredients": ["9 cups flour"], "steps": []}]}))

        original = extract.ollama
        extract.ollama = fake_ollama
        try:
            with tempfile.TemporaryDirectory() as folder:
                book = os.path.join(folder, "pg1.txt")
                with open(book, "w", encoding="utf-8") as handle:
                    handle.write(BOOK)
                out, rejects = os.path.join(folder, "out.jsonl"), os.path.join(folder, "rejects.jsonl")
                code = extract.main([book, "--host", "http://model-host:11434", "--model", "qwen2.5:14b-instruct", "--out", out, "--rejects", rejects])
                with open(out, encoding="utf-8") as handle:
                    kept = [json.loads(line) for line in handle]
                with open(rejects, encoding="utf-8") as handle:
                    dropped = [json.loads(line) for line in handle]
        finally:
            extract.ollama = original

        self.assertEqual(code, 0)
        self.assertEqual(asked, [("http://model-host:11434", "qwen2.5:14b-instruct")])
        self.assertEqual(kept[0]["name"], "Gingerbread")
        self.assertEqual(dropped[0]["name"], "Muffins")


class ReviewTests(unittest.TestCase):
    def test_a_step_that_states_an_amount_the_book_does_not_is_dropped(self):
        record = {"name": "Gingerbread", "ingredients": ["1 cup molasses", "½ cup butter", "2 teaspoonfuls ginger", "3 cups flour"],
                  "steps": ["Mix three ounces of butter with the molasses; add the ginger and flour.", "Bake in a shallow pan."]}

        result = extract.extract(BOOK, stand_in([json.dumps({"recipes": [record]})]))

        self.assertEqual(result.kept, [])
        self.assertTrue(result.rejects[0]["reason"].startswith("fidelity: Mix three ounces"), result.rejects[0]["reason"])

    def test_a_line_whose_number_stands_only_in_another_recipe_or_another_line_is_dropped(self):
        self.assertIsNotNone(extract.fidelity("2 cups flour", BOOK))
        chunk = extract.chunks(BOOK)[0]
        record = {"name": "Corn Muffins", "ingredients": ["2 cups corn meal", "1 cup flour", "2 eggs", "3 cups molasses"], "steps": []}
        reason = extract.check_record(record, chunk, translate=False)[1]
        self.assertTrue(reason.startswith("fidelity: 3 cups molasses"), reason)

    def test_a_record_that_leaves_out_a_line_of_the_ingredient_list_is_dropped(self):
        chunk = extract.chunks(BOOK)[0]
        record = {"name": "Gingerbread", "ingredients": ["1 cup molasses", "2 teaspoonfuls ginger", "3 cups flour"], "steps": []}
        self.assertEqual(extract.check_record(record, chunk, translate=False)[1], "an ingredient line not extracted: ½ cup butter")

    def test_a_translation_that_leaves_out_an_italian_ingredient_line_is_dropped(self):
        recipe = ARTUSI[0]
        text = artusi_text([recipe])
        pairs = list(zip(recipe["english"], recipe["italian"]))
        answer = {"sourceName": recipe["title"], "name": recipe["name"],
                  "ingredients": [{"text": e, "source": i} for e, i in pairs if not i.startswith("Burro")], "steps": []}

        result = extract.extract(text, stand_in([json.dumps({"recipes": [answer]})]), translate=True)

        self.assertEqual(result.kept, [])
        self.assertEqual(result.rejects[0]["reason"], "an Italian ingredient line not translated: Burro, grammi 125.")

    def test_a_translation_that_moves_an_amount_to_another_food_is_dropped(self):
        recipe = ARTUSI[0]
        text = artusi_text([recipe])
        swapped = {"sourceName": recipe["title"], "name": recipe["name"], "steps": [], "ingredients": [
            {"text": "250 grams of flour", "source": "Farina, grammi 250."},
            {"text": "125 grams of sugar", "source": "grammi 125"},
            {"text": "110 grams of butter", "source": "grammi 110"},
            {"text": "1 egg and one yolk", "source": "Uova, N. 1 e un rosso."}]}
        whole_lines = dict(swapped, ingredients=[
            {"text": "250 grams of flour", "source": "Farina, grammi 250."},
            {"text": "125 grams of sugar", "source": "Burro, grammi 125."},
            {"text": "110 grams of butter", "source": "Zucchero, grammi 110."},
            {"text": "1 egg and one yolk", "source": "Uova, N. 1 e un rosso."}])

        result = extract.extract(text, stand_in([json.dumps({"recipes": [swapped, whole_lines]})]), translate=True)

        self.assertEqual(result.kept, [])
        self.assertEqual([r["reason"].split(":")[0] for r in result.rejects], ["an Italian line that names no food", "number check"])
        self.assertIn("names burro", result.rejects[1]["reason"])

    def test_a_translated_step_must_carry_its_quote_s_amounts_and_foods(self):
        self.assertIsNotNone(extract.number_check("Rub three ounces of butter into the flour.", "si strofina il burro"))
        self.assertIsNotNone(extract.number_check("three ounces of butter", "tre uova"))
        self.assertIsNotNone(extract.food_check("100 grams of sugar", "grammi 100 di burro", ingredient=False))
        for recipe in ARTUSI:
            for italian, english in zip(recipe["italian"], recipe["english"]):
                self.assertIsNone(extract.food_check(english, italian, ingredient=True), f"{english} / {italian}")

    def test_two_records_that_point_to_one_recipe_are_both_left_unpinned_and_the_quoted_title_comes_first(self):
        pins = {"risotto with mushrooms": 7, "risotto coi funghi": 8, "risotto alla milanese": 9}
        records = [{"name": "Risotto with mushrooms", "sourceName": "Risotto coi funghi"},
                   {"name": "Risotto with mushrooms", "sourceName": "Risotto ai funghi secchi"},
                   {"name": "Risotto Milanese style", "sourceName": "Risotto alla milanese"},
                   {"name": "Risotto with mushrooms"}]

        count = extract.pin(records, pins)

        self.assertEqual([r.get("replacesRecipeId") for r in records], [None, None, 9, None])
        self.assertEqual(count, 1)


if __name__ == "__main__":
    unittest.main()

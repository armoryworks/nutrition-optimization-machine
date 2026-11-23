"""
Tests for ops/container-era-facts.py, the miner written apart from the C# corpus tiers
(ContainerCorpusSizes) that turns a capture of old and undated books' sized container lines into
the container-era facts.
Run: python3 -m unittest discover -s ops/tests
"""

import contextlib
import importlib.util
import io
import os
import tempfile
import unittest

OPS = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
GOLDEN = os.path.join(OPS, "..", "nom-api", "Nom.Api.Tests", "Fixtures", "Golden")


def _load():
    spec = importlib.util.spec_from_file_location("container_era_facts", os.path.join(OPS, "container-era-facts.py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


ef = _load()

HEADER = "id\tbook\tera\tfdcId\tline\tquantity\tunit\tdensity\n"


def run(rows):
    with tempfile.NamedTemporaryFile("w", suffix=".tsv", delete=False, encoding="utf-8") as f:
        f.write(HEADER + "".join("\t".join(r) + "\n" for r in rows))
        path = f.name
    try:
        out = io.StringIO()
        with contextlib.redirect_stdout(out):
            ef.main([path])
        lines = out.getvalue().splitlines()
        head = lines[0].split("\t")
        return [dict(zip(head, l.split("\t"))) for l in lines[1:]]
    finally:
        os.unlink(path)


class FixtureTests(unittest.TestCase):
    def test_the_committed_facts_are_what_the_miner_gives_for_the_committed_lines(self):
        out = io.StringIO()
        with contextlib.redirect_stdout(out):
            ef.main([os.path.join(GOLDEN, "container-era-lines.tsv")])
        with open(os.path.join(GOLDEN, "container-era-facts.tsv"), encoding="utf-8") as f:
            self.assertEqual(out.getvalue(), f.read())


class RuleTests(unittest.TestCase):
    def test_a_book_s_own_sized_line_gives_the_size_of_its_package(self):
        facts = run([["l1", "Book A", "historic", "173418", "1 3-oz. package cream cheese", "3", "Ounce", "-"]])
        self.assertEqual([(f["tier"], f["grams"], f["verdict"]) for f in facts], [("book", "85", "counts"), ("era", "85", "fewer than two books")])

    def test_two_sizes_stated_once_each_in_one_book_give_no_size(self):
        facts = run([["l1", "Book A", "historic", "1", "1 (3 oz) package cream cheese", "3", "Ounce", "-"],
                     ["l2", "Book A", "historic", "1", "1 (8 oz) package cream cheese", "8", "Ounce", "-"]])
        self.assertEqual(facts[0]["verdict"], "runner-up too close")

    def test_an_era_counts_each_book_once_per_size(self):
        rows = [["l%d" % i, "Book A", "historic", "1", "1 (8 oz) package cream cheese", "8", "Ounce", "-"] for i in range(5)]
        rows += [["m1", "Book B", "historic", "1", "1 (3 oz) package cream cheese", "3", "Ounce", "-"],
                 ["m2", "Book C", "historic", "1", "1 (3 oz) package cream cheese", "3", "Ounce", "-"]]
        era = [f for f in run(rows) if f["tier"] == "era"][0]
        self.assertEqual((era["grams"], era["sizes"], era["lines"], era["books"], era["verdict"]), ("85", "85:2;227:1", "7", "3", "counts"))
        self.assertEqual((era["min"], era["max"]), ("85", "226.8"))

    def test_a_book_size_must_hold_more_than_half_of_the_book_s_lines(self):
        rows = [["l%d" % i, "Book A", "historic", "1", "1 (%s oz) package cream cheese" % oz, oz, "Ounce", "-"] for i, oz in enumerate(["3", "3", "6", "8"])]
        book = [f for f in run(rows) if f["tier"] == "book"][0]
        self.assertEqual((book["band"], book["votes"], book["runnerUp"], book["verdict"]), ("2", "4", "1", "no clear mode"))

    def test_one_book_stating_two_sizes_in_one_band_is_not_an_era_size(self):
        facts = run([["l1", "Book A", "historic", "1", "1 (16 oz) jar honey", "16", "Ounce", "-"],
                     ["l2", "Book A", "historic", "1", "1 (16.5 oz) jar honey", "16.5", "Ounce", "-"]])
        era = [f for f in facts if f["tier"] == "era"][0]
        self.assertEqual((era["band"], era["votes"], era["books"], era["verdict"]), ("2", "2", "1", "fewer than two books"))
        two = run([["l1", "Book A", "historic", "1", "1 (16 oz) jar honey", "16", "Ounce", "-"],
                   ["l2", "Book B", "historic", "1", "1 (16.5 oz) jar honey", "16.5", "Ounce", "-"]])
        self.assertEqual([f["verdict"] for f in two if f["tier"] == "era"], ["counts"])

    def test_the_grams_of_one_container_divide_the_stored_quantity_by_the_leading_count(self):
        self.assertEqual(ef.grams_per_container({"line": "2 (15 oz) cans beans", "quantity": "30", "unit": "Ounce"}), 425.2)
        self.assertEqual(ef.grams_per_container({"line": "1 1/2 (8 oz) packages", "quantity": "12", "unit": "Ounce"}), 226.8)
        self.assertEqual(ef.grams_per_container({"line": "1 (1 cup) jar", "quantity": "1", "unit": "Cup", "density": "1.0"}), 236.6)
        self.assertIsNone(ef.grams_per_container({"line": "1 (1 cup) jar", "quantity": "1", "unit": "Cup", "density": "-"}))

    def test_a_size_must_be_stated_by_the_count_and_the_container_word(self):
        self.assertTrue(ef.states_size("1 (3 oz) package cream cheese"))
        self.assertTrue(ef.states_size("1 package cream cheese (8-ounces), room temperature"))
        self.assertTrue(ef.states_size("10½-oz. can tomato soup"))
        self.assertFalse(ef.states_size("1/2 cup orange marmalade (You can also use currant jelly)"))
        self.assertFalse(ef.states_size("7 cups chicken stock or 4 cans (13-3/4-ounces each) chicken broth"))
        self.assertEqual(ef.grams_per_container({"line": "10½-oz. can tomato soup", "quantity": "10.5", "unit": "Ounce"}), 297.7)
        self.assertEqual(ef.grams_per_container({"line": "½ (6-oz.) can tomato paste", "quantity": "3", "unit": "Ounce"}), 170.1)

    def test_a_size_before_one_container_is_one_container_and_before_plural_ones_is_no_size(self):
        self.assertTrue(ef.states_size("1 lb. can salmon"))
        self.assertEqual(ef.grams_per_container({"line": "1 lb. can salmon", "quantity": "1", "unit": "Pound"}), 453.6)
        self.assertFalse(ef.states_size("2 lb. cans tomatoes"))
        self.assertFalse(ef.states_size("1 lb. canned tomatoes"))
        facts = run([["l1", "Book A", "historic", "1", "2 lb. cans tomatoes", "2", "Pound", "-"]])
        self.assertEqual(facts, [])

    def test_a_size_first_line_in_pounds_and_ounces_or_led_by_a_number_word_is_one_container(self):
        self.assertEqual(ef.grams_per_container({"line": "1 lb. 13 oz. can peaches", "quantity": "29", "unit": "Ounce"}), 822.1)
        self.assertEqual(ef.grams_per_container({"line": "one pound can salmon", "quantity": "1", "unit": "Pound"}), 453.6)
        self.assertEqual(ef.grams_per_container({"line": "a pound can of coffee", "quantity": "1", "unit": "Pound"}), 453.6)
        self.assertEqual(ef.grams_per_container({"line": "Two-pound can of pineapples", "quantity": "2", "unit": "Pound"}), 907.2)
        for line in ("1 lb. 13 oz. can peaches", "one pound can salmon", "a pound can of coffee", "Two-pound can of pineapples", "one and a half pound can tomatoes"):
            self.assertTrue(ef.states_size(line), line)
        for line in ("two pound cans salmon", "a pound of canned salmon", "one pound canned salmon", "1 lb. 13 oz. cans peaches"):
            self.assertFalse(ef.states_size(line), line)

    def test_web_modern_numbered_and_wordless_lines_are_not_mined(self):
        facts = run([["a", "-", "historic", "1", "1 (3 oz) package cream cheese", "3", "Ounce", "-"],
                     ["b", "Book M (1975)", "modern", "1", "1 (3 oz) package cream cheese", "3", "Ounce", "-"],
                     ["c", "Book A", "historic", "1", "1 No. 2 can corn (20 oz)", "20", "Ounce", "-"],
                     ["d", "Book A", "historic", "1", "3 oz cream cheese", "3", "Ounce", "-"]])
        self.assertEqual(facts, [])

    def test_a_line_that_states_no_single_size_of_one_container_is_not_read(self):
        for line in ("1 (7½ to 8 oz.) can taco sauce", "1 8-oz. or 1-lb. can salmon", "one or two 2-oz. cans anchovies",
                     "1 package (3 to 4 ounces) pudding mix", "1 package (8-10 ounces) riced cauliflower", "1 can tomatoes (14-15 ounces, diced)",
                     "1 can chopped green chilies (1-4.25 oz. can)", "1 half pint can of condensed milk", "one pint and a half can of pineapple",
                     "1 13³⁄₄ oz. package hot roll mix", "1 qt. jar with fresh mint leaves"):
            self.assertFalse(ef.one_size(line), line)
        for line in ("three one-pound cans of salmon", "2 seven-ounce cans tuna", "1 qt. can of tomatoes", "a small (quarter of a pound) box of mustard"):
            self.assertTrue(ef.one_size(line) and ef.states_size(line), line)

    def test_a_flavoured_product_does_not_size_a_plain_food(self):
        self.assertFalse(ef.names_its_food("2 (3 oz.) pkg. raspberry-flavored gelatin", "Gelatins, dry powder, unsweetened"))
        self.assertFalse(ef.names_its_food("3 packages strawberry gelatin (3 oz. packages)", "Gelatins, dry powder, unsweetened"))
        self.assertTrue(ef.names_its_food("1 (3 oz) package lime gelatin", "Gelatin desserts, dry mix"))
        self.assertTrue(ef.names_its_food("1 oz. packet of gelatine", "Gelatins, dry powder, unsweetened"))

    def test_a_web_recipe_with_a_book_attribution_is_modern_unless_its_era_row_says_otherwise(self):
        self.assertEqual(ef.era_of({"era": "unknown", "host": "www.myplate.gov"}), "modern")
        self.assertEqual(ef.era_of({"era": "unknown", "host": "www.gutenberg.org"}), "unknown")
        self.assertEqual(ef.era_of({"era": "unknown", "host": "-"}), "unknown")
        self.assertEqual(ef.era_of({"era": "historic", "host": "www.myplate.gov"}), "historic")
        self.assertEqual(ef.era_of({"era": "unknown"}), "unknown")

    def test_the_committed_readings_are_what_the_miner_reads_line_by_line(self):
        out = io.StringIO()
        with contextlib.redirect_stdout(out):
            ef.main(["--lines", os.path.join(GOLDEN, "container-era-lines.tsv")])
        with open(os.path.join(GOLDEN, "container-era-readings.tsv"), encoding="utf-8") as f:
            self.assertEqual(out.getvalue(), f.read())

    def test_a_size_word_right_before_the_container_word_is_its_own_key(self):
        self.assertEqual(ef.size_word("1 large package (8 oz.) cream cheese"), "large")
        self.assertEqual(ef.size_word("1 large (8 oz) package cream cheese"), "")
        self.assertEqual(ef.kind_of("2 pkgs. cream cheese"), "package")
        self.assertEqual(ef.kind_of("1 tin salmon"), "can")


if __name__ == "__main__":
    unittest.main()

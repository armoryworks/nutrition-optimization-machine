"""
Tests for the reading of the two hand imports that failed on staging (checkpoint 7): the Library of
Congress books, whose own OCR garbles dot-leader ingredient tables (S22), and NCI's "Down Home Healthy
Cooking" (S23). The readability test, the page images read where the Library's text fails it, the
rule reader that splits old cookbooks' regular recipes without the model, the order of the books and
the model calls left per book, the call budget and the queue the two runs share on the model host;
NCI's letter-spaced titles, printed yields, yield sentences, mixed numbers and wrapped lines.
The NCI pages are U.S. government text (public domain), copied from the staging text step. The
Library pages are made for the test in the layout of the books, with the garbled lines staging found.
No network: the sites and the model are stand-ins; the word list is a fixture taken from SCOWL's
american-english (Debian wamerican).
Run: python3 -m unittest discover -s ops/tests
"""

import contextlib
import importlib.util
import io
import json
import os
import re
import sys
import tempfile
import threading
import time
import unittest

OPS = os.path.join(os.path.dirname(__file__), "..")
_spec = importlib.util.spec_from_file_location("hand_import_reading", os.path.join(OPS, "hand-import.py"))
hand = importlib.util.module_from_spec(_spec)
sys.modules["hand_import_reading"] = hand
_spec.loader.exec_module(hand)
cookbook = hand.cookbook

FIXTURES = os.path.join(os.path.dirname(__file__), "fixtures", "hand-import")
cookbook.WORD_LISTS = (os.path.join(FIXTURES, "words.txt"),)
WORDS = cookbook.load_words()
SOURCES = hand.load_sources()

with open(os.path.join(FIXTURES, "nci-pages.json"), encoding="utf-8") as _handle:
    NCI = json.load(_handle)
NCI_PAGE = {p["n"]: p["text"] for p in NCI["pages"]}
with open(os.path.join(FIXTURES, "loc-book.text.json"), encoding="utf-8") as _handle:
    LOC_TEXT = json.load(_handle)

GARBLED = ["ELI aa a as I pint", "Shy SS 0 Ut ee ae ay cee ise cecil (Oude B:", "ALIN Gg | 0) Ea aaa ian a ne I pint",
           "BI ttace ee Sore Semel arg ys 1 ae 3 qts."]


def loc_doc(pages=None):
    """The Library fixture book as the text step writes it: the Library's own OCR, running heads taken off."""
    keys = sorted(LOC_TEXT, key=int) if pages is None else [str(n) for n in pages]
    texts = hand.strip_running_heads([LOC_TEXT[k]["fulltext"] for k in keys])
    return {"doc": "loc-lizzie", "source": "loc", "url": "https://www.loc.gov/item/lizzie/", "resourceUrl": "https://www.loc.gov/resource/gdc.lizzie/",
            "title": "Lizzie's cook book", "sha256": "ab", "layout": "book",
            "pages": [{"n": int(k), "method": "loc-ocr" if t.strip() else "loc-blank", "text": t} for k, t in zip(keys, texts)]}


def nci_doc(*numbers):
    return {**NCI, "pages": [p for p in NCI["pages"] if p["n"] in numbers]}


class ReadabilityTests(unittest.TestCase):
    def test_the_librarys_ocr_of_a_dot_leader_table_is_not_readable_and_real_lines_are(self):
        for line in GARBLED:
            self.assertIsNotNone(cookbook.unreadable(line, WORDS, ingredient=True), line)
        self.assertIsNotNone(cookbook.unreadable("I pint", WORDS, ingredient=True), "an amount with nothing it measures")
        for line in ("Corn meal . . . . . . . . 1 pint", "Saleratus, 1 teaspoonful", "Butter, size of an egg.",
                     "1½ tablespoon Hot ‘n Spicy Seasoning (see recipe on page 19)", "6 lean center–cut pork chops, ½ inch thick",
                     "¾ cup cornflake crumbs", "5 cups unpeeled, sliced apples", "Mix and bake."):
            self.assertIsNone(cookbook.unreadable(line, WORDS, ingredient=True), line)

    def test_every_line_of_the_nci_recipe_pages_reads_but_their_letter_spaced_titles(self):
        for number, page in NCI_PAGE.items():
            for line in page.split("\n"):
                if line.strip() and cookbook.unreadable(line, WORDS):
                    heading = re.sub(r"[^A-Z]", "", line) in hand.PAGE_HEADINGS
                    self.assertTrue(hand._title_line(line) or heading or len(line.strip()) <= 3, f"page {number}: {line!r}")

    def test_a_record_that_copies_the_garbled_lines_verbatim_passes_the_number_check_and_fails_the_readability_test(self):
        page = "CORN BREAD.\n\n" + "\n".join(GARBLED) + "\n\nMix the meal and flour, add the eggs well beaten and the milk; bake\nin a quick oven twenty minutes.\n"
        chunk = cookbook.Chunk(0, len(page), page)
        copied = {"name": "CORN BREAD", "ingredients": GARBLED, "steps": ["Mix the meal and flour, add the eggs well beaten and the milk; bake in a quick oven twenty minutes."]}
        self.assertIsNotNone(cookbook.check_record(dict(copied), chunk, translate=False)[0],
                             "staging checkpoint 7, S22: the one Library record kept was this garbage, copied verbatim")
        record, why = cookbook.check_record(dict(copied), chunk, translate=False, words=WORDS)
        self.assertIsNone(record)
        self.assertTrue(why.startswith("unreadable text"), why)


class LibraryRuleReaderTests(unittest.TestCase):
    def test_regular_recipes_are_split_by_rule_and_only_the_prose_recipe_goes_to_the_model(self):
        asked = []

        def model(system, text):
            asked.append(text)
            return json.dumps({"recipes": [{"name": "MOLASSES CAKE", "ingredients": ["One cup of molasses"], "steps": ["Bake in a loaf."]}]})
        result = hand.extract_document(loc_doc(), SOURCES["loc"], ask=model, words=WORDS)
        kept = {r["recipe"]["name"]: r for r in result.kept}
        self.assertEqual(sorted(kept), ["GINGER SNAPS", "MOLASSES CAKE", "PICKLED PEACHES", "SPONGE CAKE"])
        self.assertEqual([i["rawLine"] for i in kept["GINGER SNAPS"]["recipe"]["ingredients"]],
                         ["1 cup Molasses", "½ cup Butter", "1 tablespoonful Ginger", "3 cups Flour"], "a dot-leader row is read amount first")
        self.assertTrue(kept["GINGER SNAPS"]["recipe"]["steps"][0]["instruction"].endswith("bake in a quick oven. Keep them in a tin box, well covered."))
        self.assertEqual(kept["SPONGE CAKE"]["pages"], [40])
        self.assertEqual(kept["SPONGE CAKE"]["recipe"]["sourceUrl"], "https://www.loc.gov/resource/gdc.lizzie/?sp=40&st=text&q=SPONGE%20CAKE")
        self.assertEqual(len(asked), 1, "only the recipe the rules cannot split is sent: one call, not one per chunk of the book")
        self.assertTrue(asked[0].startswith("MOLASSES CAKE"))
        self.assertNotIn("PREFACE", "".join(asked), "front matter with no amount and no measure is not sent")
        self.assertNotIn("INDEX", "".join(asked), "an index of dot-leader rows ending in page numbers is not sent")
        unreadable = [r for r in result.rejects if r["reason"].startswith("unreadable text")]
        self.assertEqual([(r["page"], r["name"]) for r in unreadable], [(19, "WHITE BREAD."), (31, "CORN BREAD.")],
                         "staging checkpoint 7, S22: the garbled tables are never sent to the model, whose readings were its own")
        self.assertEqual(result.unreadable_pages, [19, 31])
        self.assertEqual((result.by_rule, result.model_blocks, result.unreadable_blocks, result.model_calls, result.model_left), (3, 1, 2, 1, 0))
        self.assertNotIn("GINGER SNAPS", [r["name"] for r in result.rejects])

    def test_a_garbled_line_that_looks_like_a_title_does_not_cut_the_recipe(self):
        found = cookbook.read_by_rule("WHITE BREAD.\n\nBI Ttace Ee Sore Semel\nShy SS 0 Ut ee ae ay cee ise\nELI aa a as I pint\n\nKnead well and bake one hour.\n", WORDS)
        self.assertEqual([(b.title, b.outcome) for b in found], [("WHITE BREAD.", "unreadable")])

    def test_a_word_broken_at_a_line_end_is_joined_and_found_in_the_book(self):
        text = "GINGER SNAPS.\n\n1 cup molasses\n3 cups flour\n\nRoll very thin and bake; keep them in a tin box, well cov-\nered, and well-\nbeaten eggs may be added.\n"
        found = cookbook.read_by_rule(text, WORDS)
        self.assertEqual(found[0].outcome, "rule")
        self.assertEqual(found[0].record["steps"][0]["instruction"],
                         "Roll very thin and bake; keep them in a tin box, well covered, and well-beaten eggs may be added.")
        self.assertIsNone(cookbook.fidelity("keep them in a tin box, well covered", text))
        self.assertIsNone(cookbook.fidelity("and well-beaten eggs may be added", text))

    def test_without_a_model_host_a_book_keeps_what_the_rules_read_and_counts_the_calls_left(self):
        result = hand.extract_document(loc_doc(), SOURCES["loc"], words=WORDS)
        self.assertEqual(len(result.kept), 3)
        self.assertEqual((result.model_calls, result.model_left), (1, 1))
        self.assertEqual(result.unparsed[0]["reason"], "a book needs the model host (--host, --model)")


class LibraryPageImageTests(unittest.TestCase):
    TABLE = "CORN BREAD.\n\nCorn meal . . . . . . . . 1 pint\nFlour . . . . . . . . . . 1 pint\nSugar . . . . . . . . . . ½ cup\nMilk . . . . . . . . . . . 1 pint\n\nMix the meal and flour, add the milk; bake\nin a quick oven twenty minutes.\n"

    def write(self, folder, data):
        path = os.path.join(folder, "text.json")
        with open(path, "w", encoding="utf-8") as handle:
            json.dump(data, handle)
        return path

    def test_a_page_whose_library_text_fails_is_marked_and_its_image_read_in_the_table_mode_when_it_is_fetched(self):
        with tempfile.TemporaryDirectory() as folder:
            path = self.write(folder, {"31": LOC_TEXT["31"], "40": LOC_TEXT["40"]})
            pages, unread = hand.loc_pages(path, WORDS)
            self.assertEqual([(p["n"], p["method"], p["needsImage"]) for p in pages], [(31, "loc-ocr", True), (40, "loc-ocr", False)])
            read = []
            pages, unread = hand.loc_pages(path, WORDS, {31: "/work/loc/docs/b.p0031.jpg"}, lambda p: read.append(p) or self.TABLE)
            self.assertEqual(read, ["/work/loc/docs/b.p0031.jpg"], "only the page that fails is read from its image")
            self.assertEqual((pages[0]["method"], pages[0]["text"], pages[0]["needsImage"]), ("loc-image-ocr", self.TABLE, False))
            self.assertGreater(pages[0]["libraryReading"]["unreadable"], pages[0]["imageReading"]["unreadable"])
            pages, unread = hand.loc_pages(path, WORDS, {31: "/x.jpg"}, lambda p: "\n".join(GARBLED * 2))
            self.assertEqual(pages[0]["method"], "loc-ocr", "the image's reading is taken only when it reads better")
            pages, unread = hand.loc_pages(path, WORDS, {31: "/x.jpg"}, lambda p: None)
            self.assertEqual(unread, [31], "an image fetched and not read here (no tesseract) is reported, never only skipped")
        self.assertEqual(hand.page_reading(self.TABLE, WORDS), {"shortLines": 7, "unreadable": 0, "marked": 0})

    def test_the_page_image_is_the_largest_jpeg_wide_enough_else_the_master(self):
        item = {"resources": [{"files": [
            [{"mimetype": "image/gif", "url": "https://tile.loc.gov/a/0001.gif"},
             {"mimetype": "image/jpeg", "url": "https://tile.loc.gov/a/0001v.jpg", "width": 640},
             {"mimetype": "image/jpeg", "url": "https://tile.loc.gov/a/0001r.jpg", "width": 2400},
             {"mimetype": "image/jp2", "url": "https://tile.loc.gov/a/0001.jp2", "width": 3000}],
            [{"mimetype": "image/jpeg", "url": "https://tile.loc.gov/a/0002v.jpg", "width": 640},
             {"mimetype": "image/jp2", "url": "https://tile.loc.gov/a/0002.jp2"}],
            [{"mimetype": "text/plain", "url": "https://tile.loc.gov/a/0003.txt"}]]}]}
        self.assertEqual(hand.loc_page_images(item), {1: "https://tile.loc.gov/a/0001r.jpg", 2: "https://tile.loc.gov/a/0002.jp2"})

    def test_a_cut_off_jpeg_is_refused(self):
        with tempfile.TemporaryDirectory() as folder:
            path = os.path.join(folder, "p.jpg")
            with open(path, "wb") as handle:
                handle.write(b"\xff\xd8\xff\xe0" + b"\0" * 64)
            with self.assertRaises(hand.Refused):
                hand.verify_file(path, "image/jpeg")


class NciPageTests(unittest.TestCase):
    def test_the_titles_the_pages_print_are_read_by_rule(self):
        self.assertEqual({n: hand.page_title(t, WORDS) for n, t in NCI_PAGE.items()}, {
            9: "BLACK SKILLET BEEF with GREENS and RED POTATOES",
            13: "BAKED FRIED CHICKEN BREAST with MIXED VEGETABLES",
            14: "20-MINUTE CHICKEN CREOLE",
            18: "BAKED PORK CHOPS",
            21: "HOT’N SPICY SEASONING",
            29: "MIXED GREENS",
        })

    def test_the_model_name_gives_way_to_the_printed_title_and_the_printed_yield_is_kept(self):
        answer = {"name": "MIXED DEGREE GREENS", "ingredients": ["2 bunches mustard greens or kale", "2 bunches turnip greens",
                                                                  "pepper to taste (optional)", "1 teaspoon salt, or to taste (optional)"],
                  "steps": ["Rinse greens well, removing stems.", "Serve with some of the pot liquor (liquid from the cooked greens)."]}
        record, why = hand.paged_record(answer, NCI_PAGE[29], WORDS)
        self.assertIsNone(why)
        self.assertEqual(record["name"], "MIXED GREENS", "staging checkpoint 7, S23: the model invented DEGREE")
        self.assertEqual((record["recipeYield"], record["recipeServings"]), ("8 servings", 8.0), "S23: no record kept its printed yield")
        chicken = {"name": "Baked Fried Chicken with Mixed Vegetables", "ingredients": ["non-stick cooking spray", "½ cup plain dried bread crumbs",
                   "½ cup grated Parmesan cheese", "2 tablespoons cornmeal", "½ teaspoon ground red pepper", "1 large egg white", "½ teaspoon salt",
                   "1½ lbs boneless, skinless chicken breast", "3 cups mixed vegetables"], "steps": ["Pre-heat oven to 350°."]}
        record, why = hand.paged_record(chicken, NCI_PAGE[13], WORDS)
        self.assertIsNone(why)
        self.assertEqual(record["name"], "BAKED FRIED CHICKEN BREAST with MIXED VEGETABLES", "S23: page 13 lost Breast")

    def test_a_sidebar_tip_with_its_icon_letter_is_no_step_and_a_list_heading_no_ingredient_line(self):
        page = ("GARDEN BEAN SALAD\n\nINGREDIENTS\n\n2 cups green beans, cooked\n1 small onion, chopped\n\nDRESSING\n\n"
                "2 tablespoons vinegar\n1 tablespoon oil\n\nZ To keep it light, use less oil.\n\n"
                "Put the beans and onion in a bowl.\nShake the vinegar and oil together and pour over.\nChill for 15 min\u00adutes.\n\n"
                "Makes 4 servings.\n")
        answer = {"name": "GARDEN BEAN SALAD", "ingredients": ["2 cups green beans, cooked", "1 small onion, chopped", "DRESSING",
                                                               "2 tablespoons vinegar", "1 tablespoon oil"],
                  "steps": ["Put the beans and onion in a bowl.", "Shake the vinegar and oil together and pour over.", "Chill for 15 min\u2013utes.",
                            "Z To keep it light, use less oil."]}
        record, why = hand.paged_record(answer, page, WORDS)
        self.assertIsNone(why)
        self.assertEqual([i["rawLine"] for i in record["ingredients"]],
                         ["2 cups green beans, cooked", "1 small onion, chopped", "2 tablespoons vinegar", "1 tablespoon oil"],
                         "staging checkpoint 8, T19: GARDEN POTATO SALAD kept the list heading DRESSING as a line")
        self.assertEqual([s["instruction"] for s in record["steps"]],
                         ["Put the beans and onion in a bowl.", "Shake the vinegar and oil together and pour over.", "Chill for 15 minutes."],
                         "T19: CATFISH STEW kept the sidebar tip with its icon letter as step 5, and a soft hyphen came back as a dash")
        self.assertEqual(hand.joined("low\u2013fat milk", WORDS), "low\u2013fat milk", "two words joined by a dash stay apart")

    def test_a_yield_sentence_is_no_step_and_is_the_yield(self):
        answer = {"name": "HOT'N SPICY SEASONING", "ingredients": ["¼ cup paprika", "2 tablespoons dried oregano, crushed", "2 teaspoons chili powder",
                  "1 teaspoon garlic powder", "1 teaspoon black pepper", "½ teaspoon red (cayenne) pepper", "½ teaspoon dry mustard"],
                  "steps": ["Mix together all ingredients.", "Store in airtight container.", "Makes about ¹/³ cup."]}
        record, why = hand.paged_record(answer, NCI_PAGE[21], WORDS)
        self.assertIsNone(why)
        self.assertEqual([s["instruction"] for s in record["steps"]], ["Mix together all ingredients.", "Store in airtight container."],
                         "S23: the yield sentence was taken as step 3")
        self.assertEqual(record["recipeYield"], "about ¹/³ cup")
        self.assertNotIn("recipeServings", record)
        self.assertIsNone(hand.page_yield("Makes a thick sauce."), "a step that names no amount is no yield")

    def test_a_step_that_only_opens_with_the_yield_keeps_its_method_and_the_yield_is_the_first_sentence(self):
        self.assertEqual(hand.page_yield("Makes 12 muffins. Bake 20 minutes at 400 °F."), "12 muffins",
                         "review of ck16-hand-import (CK16-R3): the baking instruction was taken as the yield")
        self.assertEqual(hand.page_yield("Makes 1.5 cups."), "1.5 cups")
        answer = {"name": "MIXED GREENS", "ingredients": ["2 bunches mustard greens or kale", "2 bunches turnip greens",
                  "pepper to taste (optional)", "1 teaspoon salt, or to taste (optional)"],
                  "steps": ["Rinse greens well, removing stems.", "Makes 8 servings. Serve with some of the pot liquor (liquid from the cooked greens)."]}
        record, why = hand.paged_record(answer, NCI_PAGE[29], WORDS)
        self.assertIsNone(why)
        self.assertEqual([s["instruction"] for s in record["steps"]],
                         ["Rinse greens well, removing stems.", "Serve with some of the pot liquor (liquid from the cooked greens)."],
                         "CK16-R3: a step that opened with the yield was dropped whole")
        self.assertEqual(record["recipeYield"], "8 servings")

    def test_a_mixed_number_set_with_a_no_break_space_and_a_line_wrapped_in_a_narrow_column_are_found(self):
        chops = {"name": "BAKED PORK CHOPS", "ingredients": ["6 lean center–cut pork chops, ½ inch thick", "1 egg white", "1 cup evaporated skim milk",
                 "¾ cup cornflake crumbs", "1 ¼ cup fine dry bread crumbs", "2 tablespoons Hot 'n Spicy Seasoning (see recipe on page 19)",
                 "1½ teaspoon salt", "nonstick spray coating"], "steps": ["Trim all fat from chops."]}
        record, why = hand.paged_record(chops, NCI_PAGE[18], WORDS)
        self.assertIsNone(why, "staging checkpoint 7, S23: '1 ¼' with a no-break space was read as 5/4 against the page's 1¼")
        self.assertEqual(cookbook.numbers("1 ¼ cup"), cookbook.numbers("1¼ cup"))
        beef = {"name": "BEEF with GREENS and RED POTATOES", "ingredients": ["1 lb beef top round",
                "1½ tablespoon Hot 'n Spicy Seasoning (see recipe on page 19)", "8 red-skinned potatoes, halved", "3 cups finely chopped onion",
                "2 cups beef broth", "2 large cloves garlic, minced", "2 large carrots, peeled, cut into very thin 2½-inch strips",
                "2 bunches (½ lb each) mustard greens, kale, or turnip greens, stems removed, coarsely torn", "non-stick cooking spray"],
                "steps": ["Partially freeze beef."]}
        record, why = hand.paged_record(beef, NCI_PAGE[9], WORDS)
        self.assertIsNone(why, "S23: p. 9 was dropped for a line set over three lines of a narrow column")
        self.assertEqual(record["name"], "BLACK SKILLET BEEF with GREENS and RED POTATOES")

    def test_the_titles_own_lines_are_no_ingredient_lines_and_a_name_without_a_printed_title_must_stand_on_the_page(self):
        creole = {"name": "20-Minute Chicken Creole", "ingredients": [
            "4 medium chicken breast halves (1½ lbs total) skinned, boned, and cut into 1-inch strips", "1, 14-ounce can tomatoes, cut up",
            "1 cup low-sodium chili sauce", "1½ cups chopped green pepper (1 large)", "½ cup chopped celery", "¼ cup chopped onion",
            "2 cloves garlic, minced", "1 tablespoon chopped fresh basil or 1 teaspoon dried basil, crushed",
            "1 tablespoon chopped fresh parsley or 1 teaspoon dried parsley", "¼ teaspoon crushed red pepper", "¼ teaspoon salt",
            "non-stick cooking spray"], "steps": ["Spray deep skillet with non-stick spray coating."]}
        record, why = hand.paged_record(creole, NCI_PAGE[14], WORDS)
        self.assertIsNone(why, "S23: the title's digits (2 0 - M I N U T E) were read as an ingredient line the record left out")
        self.assertEqual((record["name"], record["recipeServings"]), ("20-MINUTE CHICKEN CREOLE", 4.0))
        untitled = "INGREDIENTS\n\n1 cup rice\n\n2 cups water\n\nBoil the rice in the water for 20 minutes."
        made_up = {"name": "Savory Rice", "ingredients": ["1 cup rice", "2 cups water"], "steps": ["Boil the rice in the water for 20 minutes."]}
        self.assertTrue(hand.paged_record(made_up, untitled, WORDS)[1].startswith("a word of the name not on the page"))
        plain = {"name": "Rice in Water", "ingredients": ["1 cup rice", "2 cups water"], "steps": ["Boil the rice in the water for 20 minutes."]}
        self.assertIsNone(hand.paged_record(plain, untitled, WORDS)[1])


class LongRunTests(unittest.TestCase):
    def books(self, folder):
        rows = [("loc-a", "https://www.loc.gov/item/a/"), ("loc-b", "https://www.loc.gov/item/b/")]
        path = os.path.join(folder, "books.csv")
        with open(path, "w", encoding="utf-8") as handle:
            handle.write("doc_id,source,kind,url,layout,title,attribution\n")
            for doc_id, url in rows:
                handle.write(f"{doc_id},loc,loc-item,{url},book,{doc_id},Library of Congress. {doc_id}\n")
        os.makedirs(os.path.join(folder, "text"), exist_ok=True)
        prose = "MOLASSES CAKE.\n\nOne cup of molasses, one cup of sugar and three cups of flour. Bake in a loaf.\n\n"
        regular = "SPONGE CAKE.\n\n3 eggs.\n1 cup of sugar.\n\nBeat the eggs and sugar together ten minutes, and bake half an hour.\n\n"
        docs = {"loc-a": [(1, prose.replace("MOLASSES CAKE", "GINGER CAKE")), (2, regular)],
                "loc-b": [(1, regular), (2, regular.replace("SPONGE", "WHITE")), (3, prose)]}
        for doc_id, pages in docs.items():
            with open(os.path.join(folder, "text", f"{doc_id}.pages.json"), "w", encoding="utf-8") as handle:
                json.dump({"doc": doc_id, "source": "loc", "url": f"https://www.loc.gov/item/{doc_id}/", "resourceUrl": None, "title": doc_id,
                           "sha256": "ab", "layout": "book", "pages": [{"n": n, "method": "loc-ocr", "text": t} for n, t in pages]}, handle)
        return path

    def main(self, *argv):
        out, err = io.StringIO(), io.StringIO()
        with contextlib.redirect_stdout(out), contextlib.redirect_stderr(err):
            code = hand.main(list(argv))
        return code, out.getvalue(), err.getvalue()

    def test_the_plan_orders_the_books_by_how_well_the_rules_read_them_and_counts_the_calls_left(self):
        with tempfile.TemporaryDirectory() as folder:
            books = self.books(folder)
            code, out, _ = self.main("plan", "--out", folder, "--books", books, "--model", "m")
            self.assertEqual(code, 0)
            plan = json.loads(out)
            self.assertEqual([(r["doc"], r["keptByRule"], r["modelCalls"], r["modelCallsLeft"]) for r in plan["order"]],
                             [("loc-b", 2, 1, 1), ("loc-a", 1, 1, 1)])
            self.assertEqual((plan["keptByRule"], plan["modelCalls"], plan["modelCallsLeft"]), (3, 2, 2))
            with open(os.path.join(folder, "extract-plan.json"), encoding="utf-8") as handle:
                self.assertEqual(json.load(handle), plan["order"])

    def test_a_budget_of_calls_stops_the_run_keeps_every_answer_and_the_next_day_goes_on_book_by_book(self):
        asked = []
        real = (hand.HostGate, hand.cookbook.ollama)
        self.addCleanup(lambda: (setattr(hand, "HostGate", real[0]), setattr(hand.cookbook, "ollama", real[1])))

        class Free(real[0]):
            def __init__(self, ask, in_use, limit, poll, sleep=time.sleep):
                super().__init__(ask, lambda: False, limit, poll, sleep)

        def model(system, text):
            asked.append(text.split("\n", 1)[0])
            name = text.split(".", 1)[0]
            return json.dumps({"recipes": [{"name": name, "ingredients": [text.split("\n\n")[1].split(",")[0]], "steps": ["Bake in a loaf."]}]})
        hand.HostGate = Free
        hand.cookbook.ollama = lambda host, model_name, timeout=600: model
        with tempfile.TemporaryDirectory() as folder:
            books = self.books(folder)
            code, out, err = self.main("extract", "--out", folder, "--books", books, "--host", "http://model", "--model", "m", "--max-calls", "1")
            self.assertEqual(code, hand.STOPPED)
            self.assertIn("--max-calls 1", err)
            self.assertEqual(asked, ["MOLASSES CAKE."], "the book the rules read best (loc-b) comes first, then loc-a's first prose recipe")
            summary = json.loads(out)["loc"]
            self.assertEqual(summary["modelCallsLeft"], 1)
            self.assertEqual(summary["modelCallsLeftByDocument"], {"loc-a": 1})
            with open(os.path.join(folder, "records", "loc-a.status.json"), encoding="utf-8") as handle:
                status = json.load(handle)
            self.assertEqual((status["modelCalls"], status["modelCallsLeft"], status["done"]), (1, 1, False))
            with open(os.path.join(folder, "records", "loc-b.status.json"), encoding="utf-8") as handle:
                self.assertTrue(json.load(handle)["done"])
            code, out, err = self.main("extract", "--out", folder, "--books", books, "--host", "http://model", "--model", "m", "--max-calls", "1")
            self.assertEqual(code, 0)
            self.assertEqual(asked, ["MOLASSES CAKE.", "GINGER CAKE."], "an answered chunk is never asked again")
            self.assertNotIn("modelCallsLeftByDocument", json.loads(out)["loc"])

    def test_an_unreadable_answer_is_a_call_left_and_the_book_is_not_done(self):
        with tempfile.TemporaryDirectory() as folder:
            checkpoint = os.path.join(folder, "nci.answers.jsonl")
            ask = cookbook.resumable(lambda s, u: "Sorry, I cannot", checkpoint, retries=0, model="m")
            result = hand.extract_document(nci_doc(21, 29), SOURCES["nci"], ask=ask, words=WORDS)
            self.assertEqual([r["reason"].split(":")[0] for r in result.rejects], ["unreadable answer", "unreadable answer"])
            status = hand.status_row("nci", result)
            self.assertEqual((status["modelCallsLeft"], status["done"]), (2, False),
                             "review of ck16-hand-import (CK16-R1): a chunk answered unreadably is asked again, yet the book was done")
            self.assertEqual(hand.summarize({"nci": [("nci", result)]})["nci"]["modelCallsLeftByDocument"], {"nci": 2})
            planned = hand.extract_document(nci_doc(21, 29), SOURCES["nci"], ask=hand.answered_only(checkpoint, "m"), words=WORDS)
            self.assertEqual(planned.model_left, result.model_left, "the plan and the run count the same calls left")

    def test_a_marker_left_by_a_run_that_was_killed_is_no_waiting_run(self):
        with tempfile.TemporaryDirectory() as folder:
            lock = os.path.join(folder, "model-queue.lock")
            stale = f"{lock}.waiting.1.140230011"
            with open(stale, "w") as handle:
                handle.write("1")
            slept = []
            queue = hand.ModelQueue(lock, turn=1, poll=0.01, sleep=slept.append, handover=1.0)
            ask = queue.wrap(lambda s, u: "{}")
            ask("s", "1")
            ask("s", "2")
            self.assertEqual(slept, [], "review of ck16-hand-import (CK16-R2): a run killed while it waited left a marker every later run gave its turn to")
            self.assertIsNotNone(queue.handle, "the turn is kept")
            self.assertFalse(os.path.exists(stale), "the stale marker is cleared")
            queue.release()

    def test_two_runs_on_one_work_folder_take_turns_on_the_model(self):
        with tempfile.TemporaryDirectory() as folder:
            lock = os.path.join(folder, "model-queue.lock")
            order = []
            first = hand.ModelQueue(lock, turn=2, poll=0.01)
            second = hand.ModelQueue(lock, turn=2, poll=0.01)
            ask_first = first.wrap(lambda s, u: order.append(("nci", u)) or "{}")
            ask_second = second.wrap(lambda s, u: order.append(("loc", u)) or "{}")
            ask_first("s", "1")
            started = threading.Event()

            def library():
                started.set()
                for n in range(2):
                    ask_second("s", str(n))
                second.release()
            worker = threading.Thread(target=library)
            worker.start()
            started.wait()
            deadline = time.time() + 5
            while not first._others() and time.time() < deadline:
                time.sleep(0.01)
            ask_first("s", "2")
            ask_first("s", "3")
            first.release()
            worker.join(5)
            self.assertEqual(order, [("nci", "1"), ("nci", "2"), ("loc", "0"), ("loc", "1"), ("nci", "3")],
                             "staging checkpoint 7, correction 1: the two runs asked the host side by side")
        self.assertEqual(hand.queue_path("/work/nci"), "/work/model-queue.lock")
        self.assertEqual(hand.queue_path("/srv/hand/nci"), "/srv/hand/nci/model-queue.lock")


class BoxReadingTests(unittest.TestCase):
    def test_the_image_has_the_word_list_and_the_wrapper_runs_the_images_and_plan_steps(self):
        with open(os.path.join(OPS, "hand-import.Dockerfile"), encoding="utf-8") as handle:
            self.assertIn("wamerican", handle.read())
        import subprocess
        for step in ("images", "plan"):
            with tempfile.TemporaryDirectory() as work:
                done = subprocess.run(["bash", os.path.join(OPS, "hand-import-box.sh"), step], capture_output=True, text=True,
                                      env={"PATH": os.environ.get("PATH", "/usr/bin:/bin"), "NOM_HAND_DRY_RUN": "1", "NOM_HAND_WORK": work})
                self.assertEqual(done.returncode, 0, done.stderr)
                tail = {"images": f"{step} --out /work", "plan": f"{step} --out /work --model qwen2.5:14b-instruct"}[step]
                self.assertTrue(done.stdout.rstrip().endswith(tail), done.stdout)


if __name__ == "__main__":
    unittest.main()

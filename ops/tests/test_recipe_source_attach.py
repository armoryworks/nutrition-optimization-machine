"""
Tests for the source attach script: reading the staging files, attribution per file, batching,
and summing what nom-api answered. No network: nom-api is a recorded stand-in.
Run: python3 -m unittest discover -s ops/tests
"""

import importlib.util
import json
import os
import sys
import tempfile
import unittest

OPS = os.path.join(os.path.dirname(__file__), "..")
_spec = importlib.util.spec_from_file_location("source_attach", os.path.join(OPS, "recipe-source-attach.py"))
attach = importlib.util.module_from_spec(_spec)
sys.modules["source_attach"] = attach
_spec.loader.exec_module(attach)


def record(name, *lines, url=None):
    data = {"name": name, "ingredients": [{"rawLine": line, "name": line} for line in lines], "steps": []}
    if url:
        data["sourceUrl"] = url
    return data


class StandIn:
    """Answers every attach call by a rule over the record name, and keeps what it was sent."""

    def __init__(self, outcome_of):
        self.calls = []
        self.outcome_of = outcome_of

    def __call__(self, path, params, body=None):
        self.calls.append((path, dict(params), body))
        results = []
        for index, rec in enumerate(body["records"]):
            outcome, recipe = self.outcome_of(rec, body.get("sourceAttribution"))
            results.append({"index": index, "name": rec["name"], "outcome": outcome, "recipeId": recipe,
                            "score": 1.0 if recipe else None, "candidates": [], "contentSha256": json.dumps(rec, sort_keys=True),
                            "documents": 1 if outcome == "matched" else 0})
        return {"stamp": f"2026-11-21T10:00:0{len(self.calls)}Z", "documents": sum(r["documents"] for r in results),
                "results": results}


class Files:
    def __init__(self):
        self.dir = tempfile.TemporaryDirectory()

    def write(self, name, lines):
        path = os.path.join(self.dir.name, name)
        with open(path, "w", encoding="utf-8") as handle:
            for line in lines:
                handle.write((line if isinstance(line, str) else json.dumps(line)) + "\n")
        return path

    def close(self):
        self.dir.cleanup()


class FileKindTests(unittest.TestCase):
    def test_kinds_come_from_the_file_name(self):
        self.assertEqual(attach.file_kind("/home/x/harvest-review/cookbook-pg12345.jsonl"), "cookbook")
        self.assertEqual(attach.file_kind("myplate-usda.jsonl"), "myplate")
        self.assertEqual(attach.file_kind("harvest-2026-10-01.jsonl"), "web")
        self.assertEqual(attach.file_kind("notes.jsonl"), "other")


class ReadTests(unittest.TestCase):
    def setUp(self):
        self.files = Files()

    def tearDown(self):
        self.files.close()

    def test_blank_lines_are_skipped_and_bad_lines_are_reported_not_sent(self):
        path = self.files.write("cookbook-pg1.jsonl", [
            record("Gingerbread", "1 cup molasses"),
            "",
            "{not json",
            json.dumps(["a list"]),
            json.dumps({"name": "No lines"}),
            json.dumps({"name": "Bad line", "ingredients": [{"quantity": 1}, None, "  "]}),
            record("Seed Cakes", "1 cup sugar"),
        ])
        errors = []

        items = list(attach.read_records(path, errors))

        self.assertEqual([i.record["name"] for i in items], ["Gingerbread", "Seed Cakes"])
        self.assertEqual([i.line for i in items], [1, 7])
        self.assertEqual([(e["line"], e["error"].split(":")[0]) for e in errors], [
            (3, "not JSON"), (4, "not an object"), (5, "no ingredient lines"), (6, "no ingredient lines")])


class AttributionTests(unittest.TestCase):
    def setUp(self):
        self.files = Files()

    def tearDown(self):
        self.files.close()

    def test_the_csv_maps_each_file_base_name_to_its_book(self):
        path = self.files.write("books.csv", [
            "file,attribution,year",
            "/srv/harvest-review/cookbook-pg1.jsonl,The Boston Cooking-School Cook Book (1896),1896",
            "cookbook-pg2.jsonl,,1910",
        ])

        books = attach.load_attributions(path)
        self.assertEqual(books.attributions("cookbook-pg1.jsonl"), ["The Boston Cooking-School Cook Book (1896)"])
        self.assertNotIn("cookbook-pg2.jsonl", books)
        self.assertEqual(books.candidates("cookbook-pg3.jsonl", "Cake"), [None])

    def test_other_column_names_can_be_given_and_a_missing_column_is_refused(self):
        path = self.files.write("books.csv", ["jsonl,book", "cookbook-pg1.jsonl,Book One"])

        self.assertEqual(attach.load_attributions(path, "jsonl", "book").attributions("cookbook-pg1.jsonl"), ["Book One"])
        with self.assertRaises(ValueError):
            attach.load_attributions(path)

    def test_a_file_may_have_two_books_and_a_row_naming_the_recipe_or_the_book_decides(self):
        path = self.files.write("books.csv", [
            "file,attribution,name,book",
            "cookbook-6429.jsonl,Many Ways for Cooking Eggs (Rorer 1907),Poached Eggs,",
            "cookbook-6429.jsonl,Many Ways for Cooking Eggs (Rorer 1937),,Eggs 1937",
            "cookbook-6429.jsonl,Many Ways for Cooking Eggs (Rorer 1937),,Eggs 1937",
        ])

        books = attach.load_attributions(path)

        self.assertEqual(books.attributions("cookbook-6429.jsonl"),
                         ["Many Ways for Cooking Eggs (Rorer 1907)", "Many Ways for Cooking Eggs (Rorer 1937)"])
        self.assertEqual(books.candidates("cookbook-6429.jsonl", "Poached Eggs"),
                         ["Many Ways for Cooking Eggs (Rorer 1907)", "Many Ways for Cooking Eggs (Rorer 1937)"])
        self.assertEqual(books.named("cookbook-6429.jsonl", "Poached Eggs"), "Many Ways for Cooking Eggs (Rorer 1907)")
        self.assertEqual(books.candidates("cookbook-6429.jsonl", "Shirred Eggs", "Eggs 1937"),
                         ["Many Ways for Cooking Eggs (Rorer 1937)", "Many Ways for Cooking Eggs (Rorer 1907)"])
        self.assertIsNone(books.named("cookbook-6429.jsonl", "Shirred Eggs"))
        self.assertEqual(books.candidates("cookbook-6429.jsonl", "Shirred Eggs"),
                         ["Many Ways for Cooking Eggs (Rorer 1907)", "Many Ways for Cooking Eggs (Rorer 1937)"])


class AttachTests(unittest.TestCase):
    def setUp(self):
        self.files = Files()

    def tearDown(self):
        self.files.close()

    def test_a_dry_run_sends_each_file_with_its_attribution_in_batches_and_sums_the_outcomes(self):
        book = self.files.write("cookbook-pg1.jsonl", [record(f"Cake {i}", "1 cup sugar") for i in range(5)])
        plate = self.files.write("myplate-usda.jsonl", [record("Bean Salad", "1 can beans", url="https://www.myplate.gov/recipes/bean-salad")])
        recipes = {"Cake 0": 10, "Cake 1": 11, "Cake 2": 12, "Bean Salad": 20}

        def outcome_of(rec, attribution):
            if rec["name"] == "Cake 3":
                return "ambiguous", None
            if rec["name"] == "Cake 4":
                return "unmatched", None
            return "matched", recipes[rec["name"]]

        stand_in = StandIn(outcome_of)
        logged = []

        summary = attach.attach([book, plate], stand_in, {"cookbook-pg1.jsonl": "Book One"}, batch_size=2, write_log=logged.append)

        self.assertEqual([len(body["records"]) for _, _, body in stand_in.calls], [2, 2, 1, 1])
        self.assertTrue(all(path == attach.ATTACH_PATH for path, _, _ in stand_in.calls))
        self.assertTrue(all(params == {"apply": "false", "newVersion": "false"} for _, params, _ in stand_in.calls))
        self.assertEqual([body["sourceAttribution"] for _, _, body in stand_in.calls], ["Book One"] * 3 + [None])
        report = summary.report()
        self.assertEqual(report["byKind"]["cookbook"], {"files": 1, "records": 5, "documents": 3, "matched": 3, "ambiguous": 1, "unmatched": 1})
        self.assertEqual(report["byKind"]["myplate"]["matched"], 1)
        self.assertEqual(report["stamp"], "2026-11-21T10:00:01Z")
        self.assertEqual(report["target"], {"records": 6, "matchedOrAttached": 4, "rate": 0.6667, "doubles": 0, "met": False})
        self.assertEqual(len(logged), 6)
        self.assertEqual(logged[3], {"file": "cookbook-pg1.jsonl", "line": 4, "name": "Cake 3", "url": None, "kind": "cookbook", "outcome": "ambiguous",
                                     "answered": "ambiguous", "stamp": "2026-11-21T10:00:02Z", "matchedBy": None, "recipeId": None,
                                     "score": None, "candidates": [], "format": None,
                                     "contentSha256": json.dumps(record("Cake 3", "1 cup sugar"), sort_keys=True),
                                     "version": None, "documents": 0})

    def test_two_different_records_of_different_batches_that_match_one_recipe_are_reported_and_fail_the_target(self):
        first = self.files.write("cookbook-pg1.jsonl", [record("Gingerbread", "1 cup molasses")])
        second = self.files.write("cookbook-pg2.jsonl", [record("Gingerbread", "2 cups molasses")])

        summary = attach.attach([first, second], StandIn(lambda rec, attribution: ("matched", 10)), {})

        report = summary.report()
        self.assertEqual(report["doubles"], {"10": [{"file": "cookbook-pg1.jsonl", "line": 1, "name": "Gingerbread", "url": None},
                                                    {"file": "cookbook-pg2.jsonl", "line": 1, "name": "Gingerbread", "url": None}]})
        self.assertEqual(report["byKind"]["cookbook"]["double-match"], 1)
        self.assertFalse(report["target"]["met"])

    def test_a_repeated_record_gives_the_same_report_whether_or_not_a_batch_boundary_falls_between_the_copies(self):
        path = self.files.write("cookbook-pg1.jsonl", [record("Gingerbread", "1 cup molasses")] * 2)

        def answer_like_nom_api(path_, params, body=None):
            results, taken = [], {}
            for index, rec in enumerate(body["records"]):
                sha = json.dumps(rec, sort_keys=True)
                outcome = "already-attached" if taken.get(10) == sha else "matched"
                taken.setdefault(10, sha)
                results.append({"index": index, "name": rec["name"], "outcome": outcome, "recipeId": 10, "contentSha256": sha,
                                "documents": 1 if outcome == "matched" else 0})
            return {"stamp": "2026-11-21T10:00:00Z", "results": results}

        reports = [attach.attach([path], answer_like_nom_api, {}, batch_size=size).report() for size in (2, 1)]

        self.assertEqual(reports[0], reports[1])
        self.assertEqual(reports[0]["byKind"]["cookbook"], {"files": 1, "records": 2, "documents": 1, "matched": 1, "already-attached": 1})
        self.assertEqual(reports[0]["doubles"], {})
        self.assertTrue(reports[0]["target"]["met"])

    def test_a_different_record_that_conflicts_with_one_an_earlier_batch_stored_is_a_double(self):
        first = self.files.write("cookbook-pg1.jsonl", [record("Gingerbread", "1 cup molasses")])
        second = self.files.write("cookbook-pg2.jsonl", [record("Gingerbread", "2 cups molasses")])
        answers = iter([("matched", "aaa"), ("conflict", "bbb")])

        def answer(path_, params, body=None):
            outcome, sha = next(answers)
            return {"stamp": "2026-11-21T10:00:00Z", "results": [
                {"index": 0, "name": "Gingerbread", "outcome": outcome, "recipeId": 10, "contentSha256": sha, "documents": 1 if outcome == "matched" else 0}]}

        report = attach.attach([first, second], answer, {}, apply=True).report()

        self.assertEqual(report["byKind"]["cookbook"]["double-match"], 1)
        self.assertNotIn("conflict", report["byKind"]["cookbook"])
        self.assertEqual(list(report["doubles"]), ["10"])

    def test_lines_that_could_not_be_read_count_against_the_target(self):
        good = [record(f"Cake {i}", "1 cup sugar") for i in range(97)]
        path = self.files.write("cookbook-pg1.jsonl", good + ["{truncated", json.dumps({"name": "No lines"}), json.dumps({"name": "x", "ingredients": [{}]})])

        summary = attach.attach([path], StandIn(lambda rec, attribution: ("matched", 100 + int(rec["name"].split()[1]))), {})

        self.assertEqual(summary.by_kind["cookbook"]["unread"], 3)
        self.assertEqual(summary.target(), {"records": 100, "matchedOrAttached": 97, "rate": 0.97, "doubles": 0, "met": False})

    def test_an_apply_that_fails_part_way_has_already_named_its_undo_stamp_and_logged_it_per_record(self):
        path = self.files.write("cookbook-pg1.jsonl", [record(f"Cake {i}", "1 cup sugar") for i in range(3)])
        stand_in = StandIn(lambda rec, attribution: ("matched", 100 + int(rec["name"].split()[1])))

        def failing(path_, params, body=None):
            if len(stand_in.calls) == 2:
                raise attach.urllib.error.HTTPError("https://nom.example.com" + path_, 502, "Bad Gateway", None, None)
            return stand_in(path_, params, body)

        logged, messages = [], []
        original = attach.log
        attach.log = messages.append
        try:
            with self.assertRaises(attach.urllib.error.HTTPError):
                attach.attach([path], failing, {}, apply=True, batch_size=1, write_log=logged.append)
        finally:
            attach.log = original

        self.assertIn("undo with: --undo-since 2026-11-21T10:00:01Z", messages[0])
        self.assertTrue(any("2026-11-21T10:00:01Z" in m and "502" in m for m in messages[1:]))
        self.assertEqual([entry["stamp"] for entry in logged], ["2026-11-21T10:00:01Z", "2026-11-21T10:00:02Z"])

    def test_the_target_is_met_at_ninety_nine_in_a_hundred_and_web_files_do_not_count_toward_it(self):
        book = self.files.write("cookbook-pg1.jsonl", [record(f"Cake {i}", "1 cup sugar") for i in range(100)])
        web = self.files.write("harvest-1.jsonl", [record("Chili", "1 lb beef", url="https://example.com/chili")])

        def outcome_of(rec, attribution):
            if rec["name"] == "Cake 0":
                return "low-score", None
            if rec["name"] == "Chili":
                return "unmatched", None
            return ("already-attached" if rec["name"] == "Cake 1" else "matched"), 100 + int(rec["name"].split()[1])

        summary = attach.attach([book, web], StandIn(outcome_of), {})

        self.assertEqual(summary.target(), {"records": 100, "matchedOrAttached": 99, "rate": 0.99, "doubles": 0, "met": True})

    def test_a_pinned_record_sends_its_recipe_in_recipe_ids_and_not_in_the_record(self):
        pinned = record("Shortcrust pastry", "100 grams of butter")
        pinned["replacesRecipeId"] = 17338
        path = self.files.write("cookbook-artusi-v2.jsonl", [pinned, record("Gingerbread", "1 cup molasses")])
        stand_in = StandIn(lambda rec, _: ("matched", 17338))

        attach.attach([path], stand_in, {}, apply=False, new_version=True)

        body = stand_in.calls[0][2]
        self.assertEqual(body["recipeIds"], [17338, None])
        self.assertNotIn("replacesRecipeId", body["records"][0])
        stand_in = StandIn(lambda rec, _: ("matched", 1))
        attach.attach([self.files.write("cookbook-plain.jsonl", [record("Gingerbread", "1 cup molasses")])], stand_in, {})
        self.assertNotIn("recipeIds", stand_in.calls[0][2])

    def test_apply_and_new_version_are_passed_on(self):
        book = self.files.write("cookbook-pg1.jsonl", [record("Gingerbread", "1 cup molasses")])
        stand_in = StandIn(lambda rec, attribution: ("matched", 10))

        summary = attach.attach([book], stand_in, {}, apply=True, new_version=True)

        self.assertTrue(summary.applied)
        self.assertEqual(stand_in.calls[0][1], {"apply": "true", "newVersion": "true"})

    def test_a_batch_larger_than_the_api_takes_is_refused(self):
        with self.assertRaises(ValueError):
            attach.attach([], StandIn(lambda rec, attribution: ("matched", 1)), {}, batch_size=501)


HARVEST = os.path.join(os.path.dirname(__file__), "fixtures", "harvest")


def harvest(name):
    return os.path.join(HARVEST, name)


class HarvestShapeTests(unittest.TestCase):
    """The three wrapped shapes of the harvest files (cookbook chunk/book/recipe, web url/scraped_at/recipe, myplate with archive_ts)."""

    def test_a_cookbook_line_is_unwrapped_with_string_lines_and_steps_flattened_and_blanks_dropped(self):
        errors = []

        items = list(attach.read_records(harvest("cookbook-90001.jsonl"), errors))

        self.assertEqual([i.record["name"] for i in items], ["Seed Cake", "Boiled Rice", "Seed Cake"])
        self.assertEqual([i.line for i in items], [1, 2, 5])
        self.assertEqual([i.book for i in items], ["A Plain Cook Book", None, "A Second Plain Cook Book"])
        self.assertEqual(items[0].record, {
            "name": "Seed Cake",
            "ingredients": [{"rawLine": line, "name": line} for line in
                            ("1 pound of flour", "1/2 pound of butter", "1/2 pound of sugar", "4 eggs", "1 ounce of caraway seeds")],
            "steps": [{"order": 1, "instruction": "Beat the butter to a cream."}, {"order": 2, "instruction": "Add the sugar and eggs."},
                      {"order": 3, "instruction": "Stir in the flour and seeds."}, {"order": 4, "instruction": "Bake two hours in a slow oven."}],
        })
        self.assertNotIn("sourceUrl", items[0].record, "a book record keeps no URL, so nom-api matches it by name and attribution")
        self.assertEqual(items[1].record["steps"], [], "null instructions are no steps")
        self.assertEqual([(e["line"], e["error"]) for e in errors], [(4, "no ingredient lines"), (6, "recipe is not an object")])

    def test_a_web_line_takes_the_wrapper_url_and_drops_null_lines_empty_steps_and_blank_keywords(self):
        errors = []

        items = list(attach.read_records(harvest("harvest-2026-10-01.jsonl"), errors))

        self.assertEqual(errors, [])
        granola = items[0].record
        self.assertEqual(granola["sourceUrl"], "https://www.example.com/recipes/granola/")
        self.assertEqual(granola["ingredients"][0], {"rawLine": "3 cups rolled oats", "name": "rolled oats", "quantity": 3, "unit": "cup"})
        self.assertEqual([i["rawLine"] for i in granola["ingredients"]], ["3 cups rolled oats", "1/2 cup cocoa powder", "1/3 cup maple syrup"])
        self.assertEqual(granola["steps"], [{"order": 1, "instruction": "Heat the oven to 300 degrees F."},
                                            {"order": 3, "instruction": "Toss everything and bake 40 minutes."}])
        self.assertEqual(granola["keywords"], ["breakfast"])
        self.assertEqual((granola["prepTimeMinutes"], granola["recipeServings"], granola["author"]), (10, 8, "A. Cook"))
        self.assertEqual(items[2].record["sourceUrl"], "https://www.example.com/recipes/chili/")

    def test_a_myplate_line_is_read_like_a_web_line(self):
        items = list(attach.read_records(harvest("myplate-usda.jsonl"), []))

        self.assertEqual(items[0].record, {
            "name": "Bean Salad",
            "ingredients": [{"rawLine": line, "name": line} for line in
                            ("1 can (15 ounces) kidney beans, drained", "1 cup chopped celery", "2 tablespoons vinegar")],
            "steps": [{"order": 1, "instruction": "Mix everything in a bowl."}, {"order": 2, "instruction": "Chill and serve."}],
            "sourceUrl": "https://www.myplate.gov/recipes/bean-salad",
        })

    def test_no_record_sent_carries_an_empty_step_or_line(self):
        stand_in = StandIn(lambda rec, attribution: ("unmatched", None))

        attach.attach([harvest(n) for n in ("cookbook-90001.jsonl", "harvest-2026-10-01.jsonl", "myplate-usda.jsonl")], stand_in, {})

        sent = [rec for _, _, body in stand_in.calls for rec in body["records"]]
        self.assertEqual(len(sent), 7)
        self.assertTrue(all(step["instruction"].strip() for rec in sent for step in rec["steps"]))
        self.assertTrue(all(line["rawLine"].strip() for rec in sent for line in rec["ingredients"]))


class BooksTests(unittest.TestCase):
    def test_each_record_of_a_file_with_two_books_is_sent_with_its_own_book_and_an_unnamed_one_tries_each(self):
        books = attach.load_attributions(harvest("books.csv"))
        first, second = "A Plain Cook Book (Example 1901)", "A Second Plain Cook Book (Example 1937)"
        recipes = {("Seed Cake", first): 1, ("Seed Cake", second): 2, ("Boiled Rice", second): 3}
        stand_in = StandIn(lambda rec, attribution: ("matched", recipes[(rec["name"], attribution)])
                           if (rec["name"], attribution) in recipes else ("unmatched", None))
        logged = []

        summary = attach.attach([harvest("cookbook-90001.jsonl")], stand_in, books, write_log=logged.append)

        sent = [(rec["name"], body["sourceAttribution"]) for _, _, body in stand_in.calls for rec in body["records"]]
        self.assertEqual(sent, [("Seed Cake", first), ("Boiled Rice", first), ("Boiled Rice", second), ("Seed Cake", second)])
        self.assertEqual({(e["name"], e["recipeId"]) for e in logged}, {("Seed Cake", 1), ("Boiled Rice", 3), ("Seed Cake", 2)})
        self.assertEqual([e.get("attribution") for e in logged if e["name"] == "Boiled Rice"], [second])
        self.assertEqual(summary.by_kind["cookbook"]["matched"], 3)
        self.assertEqual(summary.by_kind["cookbook"]["records"], 5, "two unread lines count as records not attached")

    def test_a_record_its_named_row_finds_nothing_for_is_tried_under_the_files_other_attribution(self):
        early, late = "Many Ways for Cooking Eggs (Rorer, 1907)", "Many Ways for Cooking Eggs (Rorer, S. T., 1937)"
        books = attach.Books({"cookbook-90001.jsonl": [attach.Book(early, "Boiled Rice"), attach.Book(late, "Seed Cake")]})
        stand_in = StandIn(lambda rec, attribution: ("matched", 10 + sum(map(ord, json.dumps(rec, sort_keys=True)))) if attribution == early else ("unmatched", None))
        logged = []

        summary = attach.attach([harvest("cookbook-90001.jsonl")], stand_in, books, apply=True, write_log=logged.append)

        self.assertEqual(summary.by_kind["cookbook"]["matched"], 3,
                         "staging lost 100 of cookbook-6429's records: their rows named the 1937 edition, every recipe was imported under 1907 (checkpoint 2, M13)")
        self.assertEqual({e["attribution"] for e in logged}, {early})
        sent = [(rec["name"], body["sourceAttribution"], params["apply"]) for _, params, body in stand_in.calls for rec in body["records"]]
        self.assertIn(("Boiled Rice", early, "true"), sent, "a record its named row matches is stored at once, with no extra dry call")
        self.assertNotIn(("Boiled Rice", late, "false"), sent)
        self.assertEqual([s for s in sent if s[0] == "Seed Cake" and s[1] == late], [("Seed Cake", late, "true")] * 2)

    def test_a_record_no_book_matches_keeps_its_best_answer(self):
        books = attach.Books({"cookbook-90001.jsonl": [attach.Book("Book A"), attach.Book("Book B")]})
        stand_in = StandIn(lambda rec, attribution: ("low-score", None) if attribution == "Book A" else ("unmatched", None))
        logged = []

        attach.attach([harvest("cookbook-90001.jsonl")], stand_in, books, write_log=logged.append)

        self.assertEqual({e["outcome"] for e in logged}, {"low-score"})
        self.assertEqual({e["attribution"] for e in logged}, {"Book A"})


class TwoEditionsTests(unittest.TestCase):
    EARLY, LATE = "Many Ways for Cooking Eggs (Rorer, 1907)", "Many Ways for Cooking Eggs (Rorer, S. T., 1937)"

    def setUp(self):
        self.files = Files()
        self.books = attach.Books({"cookbook-6429.jsonl": [attach.Book(self.EARLY, "Plain Omelet"),
                                                           attach.Book(self.LATE, "Plain Omelet")]})

    def tearDown(self):
        self.files.close()

    def editions(self, scores):
        """nom-api for two editions that both hold a Plain Omelet: recipe 1 is the 1907 one, recipe 2 the 1937 one."""
        held = {}
        calls = []

        def post(path_, params, body=None):
            calls.append((params["apply"], body["sourceAttribution"], [rec["description"] for rec in body["records"]]))
            recipe = 1 if body["sourceAttribution"] == self.EARLY else 2
            results = []
            for index, rec in enumerate(body["records"]):
                sha = rec["description"]
                outcome = "already-attached" if held.get(recipe) == sha else "matched"
                if params["apply"] == "true" and recipe not in held:
                    held[recipe] = sha
                results.append({"index": index, "name": rec["name"], "outcome": outcome, "recipeId": recipe,
                                "score": scores(sha, recipe), "contentSha256": sha,
                                "documents": 1 if outcome == "matched" else 0})
            return {"stamp": f"2026-11-21T10:00:{len(calls):02d}Z", "results": results}

        return post, held, calls

    def write(self, *descriptions):
        return self.files.write("cookbook-6429.jsonl", [
            {"chunk": i, "recipe": {"name": "Plain Omelet", "description": text,
                                    "ingredients": ["4 eggs", "4 tablespoons of milk"], "steps": ["Beat the eggs."]}}
            for i, text in enumerate(descriptions)])

    def test_the_same_text_in_both_editions_gives_each_edition_its_own_recipe(self):
        path = self.write("same", "same")
        post, held, _ = self.editions(lambda sha, recipe: 1.0)
        logged = []

        dry = attach.attach([path], post, self.books, write_log=logged.append)

        self.assertEqual(sorted(e["recipeId"] for e in logged), [1, 2])
        self.assertEqual(dry.by_kind["cookbook"]["matched"], 2)
        self.assertEqual(held, {})

        applied = attach.attach([path], post, self.books, apply=True)

        self.assertEqual(held, {1: "same", 2: "same"})
        self.assertEqual(applied.by_kind["cookbook"]["matched"], 2)
        self.assertTrue(applied.target()["met"])

    def test_a_record_is_stored_under_the_edition_it_matches_best_even_when_another_edition_matches_it_first(self):
        path = self.write("from 1937", "from 1907")
        own = {"from 1907": 1, "from 1937": 2}
        post, held, calls = self.editions(lambda sha, recipe: 1.0 if own[sha] == recipe else 0.8)

        summary = attach.attach([path], post, self.books, apply=True)

        self.assertEqual(held, {1: "from 1907", 2: "from 1937"})
        self.assertEqual([c for c in calls if c[0] == "true"],
                         [("true", self.EARLY, ["from 1907"]), ("true", self.LATE, ["from 1937"])])
        self.assertEqual(summary.by_kind["cookbook"]["matched"], 2)
        self.assertEqual(summary.doubles(), {})
        self.assertEqual(summary.stamp, "2026-11-21T10:00:03Z", "the undo stamp is the first stored batch's, not a dry call's")


class RescrapedTests(unittest.TestCase):
    def test_the_same_page_scraped_twice_is_stored_once_reported_and_does_not_fail_the_target(self):
        plate = harvest("myplate-usda.jsonl")
        web = harvest("harvest-2026-10-01.jsonl")
        ids = {"Chocolate Crunch Granola": 30, "Crunchy Chocolate Granola": 30, "Weeknight Chili": 31, "Bean Salad": 20}

        dry = attach.attach([plate, web], StandIn(lambda rec, attribution: ("matched", ids[rec["name"]])), {}, batch_size=1)

        report = dry.report()
        self.assertEqual(report["byKind"]["web"], {"files": 1, "records": 3, "documents": 2, "matched": 2, "rescraped": 1})
        self.assertEqual(report["doubles"], {})
        self.assertEqual(list(report["rescraped"]), ["30"])
        self.assertEqual(report["rescraped"]["30"][0]["line"], 2)
        self.assertTrue(report["target"]["met"])

        def answer_like_an_apply(path_, params, body=None):
            results = []
            for index, rec in enumerate(body["records"]):
                outcome = "conflict" if rec["name"] == "Crunchy Chocolate Granola" else "matched"
                results.append({"index": index, "name": rec["name"], "outcome": outcome, "recipeId": ids[rec["name"]],
                                "contentSha256": rec["name"], "documents": 1 if outcome == "matched" else 0})
            return {"stamp": "2026-11-21T10:00:00Z", "results": results}

        applied = attach.attach([web], answer_like_an_apply, {}, apply=True, batch_size=1).report()
        self.assertEqual(applied["byKind"]["web"]["documents"], 2)
        self.assertEqual(applied["byKind"]["web"]["rescraped"], 1)
        self.assertEqual(applied["doubles"], {})

    def test_two_different_pages_on_one_recipe_are_still_a_double(self):
        web = harvest("harvest-2026-10-01.jsonl")

        report = attach.attach([web], StandIn(lambda rec, attribution: ("matched", 30)), {}).report()

        self.assertEqual(list(report["doubles"]), ["30"])
        self.assertEqual(report["byKind"]["web"]["rescraped"], 1)
        self.assertEqual(report["byKind"]["web"]["double-match"], 1)


class RefusalTests(unittest.TestCase):
    def setUp(self):
        self.files = Files()
        self.messages = []
        self.original = attach.log
        attach.log = self.messages.append

    def tearDown(self):
        attach.log = self.original
        self.files.close()

    def test_a_record_nom_api_refuses_is_named_and_counted_and_the_rest_of_its_batch_is_sent_again(self):
        path = self.files.write("cookbook-13177.jsonl", [record(f"Cake {i}", "1 cup sugar") for i in range(3)])
        stand_in = StandIn(lambda rec, attribution: ("matched", 100 + int(rec["name"].split()[1])))

        def refusing(path_, params, body=None):
            if any(rec["name"] == "Cake 1" for rec in body["records"]):
                index = [rec["name"] for rec in body["records"]].index("Cake 1")
                raise attach.ApiError(400, json.dumps({"title": "One or more validation errors occurred.", "errors": {
                    f"Records[{index}].Steps[1].Instruction": ["The Instruction field is required."]}}))
            return stand_in(path_, params, body)

        logged = []
        summary = attach.attach([path], refusing, {}, write_log=logged.append)

        self.assertEqual([[rec["name"] for rec in body["records"]] for _, _, body in stand_in.calls], [["Cake 0", "Cake 2"]])
        self.assertEqual(summary.by_kind["cookbook"]["refused"], 1)
        self.assertEqual(summary.by_kind["cookbook"]["matched"], 2)
        self.assertEqual(summary.refused, [{"file": "cookbook-13177.jsonl", "line": 2, "name": "Cake 1",
                                            "error": "Records[1].Steps[1].Instruction: The Instruction field is required."}])
        self.assertTrue(any("cookbook-13177.jsonl line 2" in m and "Cake 1" in m for m in self.messages))
        self.assertEqual([e["outcome"] for e in logged], ["matched", "refused", "matched"])
        self.assertFalse(summary.target()["met"])

    def test_a_refusal_that_names_no_record_is_narrowed_down_to_the_record(self):
        path = self.files.write("cookbook-1.jsonl", [record(f"Cake {i}", "1 cup sugar") for i in range(4)])
        stand_in = StandIn(lambda rec, attribution: ("matched", 100 + int(rec["name"].split()[1])))

        def refusing(path_, params, body=None):
            if any(rec["name"] == "Cake 2" for rec in body["records"]):
                raise attach.ApiError(400, "Bad Request")
            return stand_in(path_, params, body)

        summary = attach.attach([path], refusing, {})

        self.assertEqual([r["name"] for r in summary.refused], ["Cake 2"])
        self.assertEqual(summary.by_kind["cookbook"]["matched"], 3)

    def test_any_other_error_still_stops_the_run(self):
        path = self.files.write("cookbook-1.jsonl", [record("Cake 0", "1 cup sugar")])

        def failing(path_, params, body=None):
            raise attach.ApiError(500, "boom")

        with self.assertRaises(attach.ApiError):
            attach.attach([path], failing, {})


class Clock:
    def __init__(self):
        self.now = 0.0
        self.slept = []

    def __call__(self):
        return self.now

    def sleep(self, seconds):
        self.slept.append(seconds)
        self.now += seconds


class PacingTests(unittest.TestCase):
    def test_calls_stay_under_the_burst_minute_and_hour_limits(self):
        clock = Clock()
        pacer = attach.Pacer(per_minute=80, per_hour=900, clock=clock, sleep=clock.sleep)
        times = []
        for _ in range(1000):
            pacer.wait()
            times.append(clock.now)
            clock.now += 0.05

        def most_in(window):
            return max(sum(1 for t in times if start <= t < start + window) for start in times)

        self.assertLessEqual(most_in(10.0), 40)
        self.assertLessEqual(most_in(60.0), 80)
        self.assertLessEqual(most_in(3600.0), 900)
        self.assertGreaterEqual(times[-1], 3600.0, "the 901st call waits for the hour")

    def test_a_429_is_waited_out_by_its_retry_after_and_the_same_call_sent_again(self):
        clock = Clock()
        pacer = attach.Pacer(clock=clock, sleep=clock.sleep)
        calls = []

        def post(path, params, body=None):
            calls.append(body)
            if len(calls) == 1:
                raise attach.ApiError(429, "", retry_after=2422)
            return {"ok": True}

        messages, original = [], attach.log
        attach.log = messages.append
        try:
            result = attach.paced(post, pacer)(attach.ATTACH_PATH, {"apply": "true"}, {"records": [1]})
        finally:
            attach.log = original

        self.assertEqual(result, {"ok": True})
        self.assertEqual(calls, [{"records": [1]}, {"records": [1]}])
        self.assertIn(2422, clock.slept)
        self.assertIn("2422", messages[0])

    def test_a_429_that_keeps_coming_stops_after_the_wait_limit(self):
        clock = Clock()

        def post(path, params, body=None):
            raise attach.ApiError(429, "", retry_after=1)

        original = attach.log
        attach.log = lambda m: None
        try:
            with self.assertRaises(attach.ApiError):
                attach.paced(post, attach.Pacer(clock=clock, sleep=clock.sleep), max_waits=2)(attach.ATTACH_PATH, {}, {})
        finally:
            attach.log = original

    def test_retry_after_reads_seconds_and_dates(self):
        self.assertEqual(attach.retry_after_seconds("2422"), 2422.0)
        self.assertIsNone(attach.retry_after_seconds(None))
        self.assertEqual(attach.retry_after_seconds("Wed, 21 Oct 2015 07:28:00 GMT"), 0.0)


class NomApiTests(unittest.TestCase):
    def test_the_key_goes_in_a_header_and_the_parameters_in_the_query(self):
        sent = {}

        class Response:
            def __enter__(self):
                return self

            def __exit__(self, *exc):
                return False

            def read(self):
                return b'{"removed": 1}'

        def fake_urlopen(req, timeout):
            sent["url"] = req.full_url
            sent["headers"] = dict(req.header_items())
            sent["method"] = req.get_method()
            return Response()

        original = attach.urllib.request.urlopen
        attach.urllib.request.urlopen = fake_urlopen
        try:
            result = attach.NomApi("https://nom.example.com/", "operator-key", None).post(
                attach.UNDO_PATH, {"sinceUtc": "2026-11-21T10:00:00Z", "apply": "false"})
        finally:
            attach.urllib.request.urlopen = original

        self.assertEqual(result, {"removed": 1})
        self.assertEqual(sent["method"], "POST")
        self.assertEqual(sent["url"], "https://nom.example.com/api/RecipeSource/attach/undo?sinceUtc=2026-11-21T10%3A00%3A00Z&apply=false")
        self.assertEqual(sent["headers"]["X-api-key"], "operator-key")

    def test_an_error_status_is_raised_with_its_body_and_retry_after(self):
        import io
        from email.message import Message

        def refusing(req, timeout):
            headers = Message()
            headers["Retry-After"] = "37"
            raise attach.urllib.error.HTTPError(req.full_url, 429, "Too Many Requests", headers, io.BytesIO(b"slow down"))

        original = attach.urllib.request.urlopen
        attach.urllib.request.urlopen = refusing
        try:
            with self.assertRaises(attach.ApiError) as caught:
                attach.NomApi("https://nom.example.com", "operator-key", None).post(attach.ATTACH_PATH, {"apply": "false"}, {"records": []})
        finally:
            attach.urllib.request.urlopen = original

        self.assertEqual((caught.exception.status, caught.exception.body, caught.exception.retry_after), (429, "slow down", 37.0))


if __name__ == "__main__":
    unittest.main()

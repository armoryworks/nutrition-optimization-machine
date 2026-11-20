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
                            "score": 1.0 if recipe else None, "candidates": [], "documents": 1 if outcome == "matched" else 0})
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
            json.dumps({"name": "Bad line", "ingredients": [{"name": "salt"}]}),
            record("Seed Cakes", "1 cup sugar"),
        ])
        errors = []

        items = list(attach.read_records(path, errors))

        self.assertEqual([i.record["name"] for i in items], ["Gingerbread", "Seed Cakes"])
        self.assertEqual([i.line for i in items], [1, 7])
        self.assertEqual([(e["line"], e["error"].split(":")[0]) for e in errors], [
            (3, "not JSON"), (4, "not an object"), (5, "ingredients are not a list of objects with rawLine"),
            (6, "ingredients are not a list of objects with rawLine")])


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

        self.assertEqual(attach.load_attributions(path), {"cookbook-pg1.jsonl": "The Boston Cooking-School Cook Book (1896)"})

    def test_other_column_names_can_be_given_and_a_missing_column_is_refused(self):
        path = self.files.write("books.csv", ["jsonl,book", "cookbook-pg1.jsonl,Book One"])

        self.assertEqual(attach.load_attributions(path, "jsonl", "book"), {"cookbook-pg1.jsonl": "Book One"})
        with self.assertRaises(ValueError):
            attach.load_attributions(path)

    def test_two_books_for_one_file_are_refused(self):
        path = self.files.write("books.csv", ["file,attribution", "cookbook-pg1.jsonl,Book One", "cookbook-pg1.jsonl,Book Two"])

        with self.assertRaises(ValueError):
            attach.load_attributions(path)


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
        self.assertEqual(logged[3], {"file": "cookbook-pg1.jsonl", "line": 4, "name": "Cake 3", "kind": "cookbook", "outcome": "ambiguous",
                                     "matchedBy": None, "recipeId": None, "score": None, "candidates": [], "format": None,
                                     "version": None, "documents": 0})

    def test_two_records_of_different_batches_that_match_one_recipe_are_reported_and_fail_the_target(self):
        first = self.files.write("cookbook-pg1.jsonl", [record("Gingerbread", "1 cup molasses")])
        second = self.files.write("cookbook-pg2.jsonl", [record("Gingerbread", "1 cup molasses")])

        summary = attach.attach([first, second], StandIn(lambda rec, attribution: ("matched", 10)), {})

        report = summary.report()
        self.assertEqual(report["doubles"], {"10": [{"file": "cookbook-pg1.jsonl", "line": 1, "name": "Gingerbread"},
                                                    {"file": "cookbook-pg2.jsonl", "line": 1, "name": "Gingerbread"}]})
        self.assertFalse(report["target"]["met"])

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

    def test_apply_and_new_version_are_passed_on(self):
        book = self.files.write("cookbook-pg1.jsonl", [record("Gingerbread", "1 cup molasses")])
        stand_in = StandIn(lambda rec, attribution: ("matched", 10))

        summary = attach.attach([book], stand_in, {}, apply=True, new_version=True)

        self.assertTrue(summary.applied)
        self.assertEqual(stand_in.calls[0][1], {"apply": "true", "newVersion": "true"})

    def test_a_batch_larger_than_the_api_takes_is_refused(self):
        with self.assertRaises(ValueError):
            attach.attach([], StandIn(lambda rec, attribution: ("matched", 1)), {}, batch_size=501)


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


if __name__ == "__main__":
    unittest.main()

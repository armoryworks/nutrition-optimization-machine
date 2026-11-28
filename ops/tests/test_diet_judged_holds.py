"""
Tests for the judged-holds loader and the readings carrier: the rows read from a judged-holds TSV,
what is sent to nom-api and how a load is undone; the readings an export or a scorer's answers
file give, with the text of an older answers file taken from its set. The committed data file
holds ids, types, food words and file names only. No network: nom-api is a recorded stand-in.
Run: python3 -m unittest discover -s ops/tests
"""

import importlib.util
import io
import json
import os
import sys
import tempfile
import unittest
from contextlib import redirect_stderr, redirect_stdout

OPS = os.path.join(os.path.dirname(__file__), "..")


def load(name, file):
    spec = importlib.util.spec_from_file_location(name, os.path.join(OPS, file))
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module


holds = load("diet_judged_holds", "diet-judged-holds.py")
readings = load("diet_readings", "diet-readings.py")

HEADER = "recipe\ttype\tfood\tjudged_by\tjudged_at\tevidence\n"
DATA = os.path.join(OPS, "diet-judged-holds", "judged-misses.tsv")
U5 = {26393, 101944, 130715, 124919, 29073, 105702, 113768}
TYPES = {"Celiac Disease", "Wheat Allergy", "Milk Allergy", "Egg Allergy", "Peanut Allergy", "Tree Nut Allergy", "Soy Allergy",
         "Sesame Allergy", "Fish Allergy", "Shellfish Allergy", "Sulfite Sensitivity"}


class Recorder:
    def __init__(self, answers=None):
        self.calls = []
        self.answers = list(answers or [])

    def post(self, path, params, body=None):
        self.calls.append((path, params, body))
        return self.answers.pop(0) if self.answers else {"stamp": "2026-11-28T10:00:00.000Z", "added": len(body or [])}

    def call(self, method, path, params, body=None):
        self.calls.append((method, path, params, body))
        return self.answers.pop(0) if self.answers else {"stamp": "2026-11-28T10:00:00.000Z", "taken": len(body or []), "received": len(body or [])}


def write(folder, name, text):
    path = os.path.join(folder, name)
    with open(path, "w", encoding="utf-8") as handle:
        handle.write(text)
    return path


class JudgedHoldsTests(unittest.TestCase):
    def test_rows_with_a_missing_column_a_bad_id_or_date_a_long_food_or_given_twice_are_not_sent(self):
        with tempfile.TemporaryDirectory() as folder:
            path = write(folder, "h.tsv", HEADER
                         + "26393\tWheat Allergy\tgrain granules\tagent-x\t2026-11-27\tdraw24.judged.tsv\n"
                         + "26393\twheat allergy\tgrain granules\tagent-x\t2026-11-27\tdraw24.judged.tsv\n"
                         + "x1\tWheat Allergy\tflour\tagent-x\t2026-11-27\tf.tsv\n"
                         + "5\tWheat Allergy\tflour\tagent-x\t27/11/2026\tf.tsv\n"
                         + "6\tWheat Allergy\t" + "a" * 61 + "\tagent-x\t2026-11-27\tf.tsv\n"
                         + "7\tWheat Allergy\t\tagent-x\t2026-11-27\tf.tsv\n")
            rows, problems = holds.read_rows(path)
        self.assertEqual([r["recipeId"] for r in rows], [26393])
        self.assertEqual(rows[0]["judgedAt"], "2026-11-27T00:00:00Z")
        self.assertEqual(len(problems), 5)

    def test_a_dry_run_sends_every_row_and_apply_and_undo_say_so(self):
        with tempfile.TemporaryDirectory() as folder:
            path = write(folder, "h.tsv", HEADER + "29073\tMilk Allergy\trich crust\tagent-ck19-diet\t2026-11-26\tck19-diet.draws.tsv\n")
            recorder = Recorder()
            with redirect_stdout(io.StringIO()), redirect_stderr(io.StringIO()):
                self.assertEqual(holds.main([path], post=recorder.post), 0)
                self.assertEqual(holds.main([path, "--apply"], post=recorder.post), 0)
                self.assertEqual(holds.main(["--undo", "2026-11-28T10:00:00.000Z"], post=recorder.post), 0)
        self.assertEqual([c[0] for c in recorder.calls], [holds.LOAD_PATH, holds.LOAD_PATH, holds.UNDO_PATH])
        self.assertEqual([c[1]["apply"] for c in recorder.calls], ["false", "true", "false"])
        self.assertEqual(recorder.calls[0][2][0]["food"], "rich crust")

    def test_the_committed_file_holds_the_known_approved_misses_and_no_recipe_text(self):
        rows, problems = holds.read_rows(DATA)
        self.assertEqual(problems, [])
        self.assertTrue(U5 <= {r["recipeId"] for r in rows}, "plan item ck23, U5: the seven approved misses are held by id")
        self.assertTrue({r["type"] for r in rows} <= TYPES)
        types_of = {}
        for row in rows:
            types_of.setdefault(row["recipeId"], set()).add(row["type"])
        self.assertIn("Wheat Allergy", types_of[113768], "ck23 review: toast judged for Celiac Disease is wheat, so a Wheat Allergy miss too")
        for row in rows:
            self.assertLessEqual(len(row["food"].split()), 3, row)
            self.assertNotIn("http", row["evidence"])
            self.assertTrue(row["judgedBy"].startswith("agent-"), "these judgements were an agent's; a person's judgement names its person")


class ReadingsTests(unittest.TestCase):
    def test_an_older_answers_file_needs_its_model_and_prompt_and_takes_its_text_from_the_set(self):
        with tempfile.TemporaryDirectory() as folder:
            os.makedirs(os.path.join(folder, "sets", "c00"))
            write(os.path.join(folder, "sets", "c00"), "diet-second-reader.tsv",
                  "# set\nid\trecipe\ttype\ttitle\tlines\tmethod\ttruth\n"
                  + "x-1\t17062\tCeliac Disease\tPilaf\t1 cup rice ¦ 2 cups water\tBoil. ¦ Serve.\tsafe\n")
            answers = write(folder, "ans.jsonl",
                            json.dumps({"recipe": 17062, "unlisted": [], "complete": True, "answers": {"Celiac Disease": ["free", ""]}}) + "\n"
                            + json.dumps({"recipe": 99, "answers": None}) + "\n")
            texts = readings.read_sets([os.path.join(folder, "sets")])
            items, problems = readings.items_of(answers, "phi4:latest", "27de91bd21cc1e9f", texts)
            self.assertEqual(texts[17062]["lines"], ["1 cup rice", "2 cups water"])
            self.assertEqual(len(items), 1)
            self.assertEqual(items[0]["text"]["method"], ["Boil.", "Serve."])
            self.assertEqual(items[0]["model"], "phi4:latest")
            self.assertEqual(len(problems), 1, "a recipe in no set has no text to sign")
            _, missing = readings.items_of(answers, None, None, texts)
            self.assertEqual(len(missing), 2)

    def test_a_signed_line_and_an_exported_reading_go_as_they_are_and_import_is_a_dry_run_unless_applied(self):
        with tempfile.TemporaryDirectory() as folder:
            path = write(folder, "r.jsonl",
                         json.dumps({"recipe": 5, "complete": True, "answers": {}, "model": "mistral-small:24b", "prompt": "p", "text": "t"}) + "\n"
                         + json.dumps({"recipeId": 6, "model": "phi4:latest", "promptSignature": "p", "textSignature": "t", "readable": True, "answers": {}}) + "\n")
            recorder = Recorder()
            with redirect_stdout(io.StringIO()), redirect_stderr(io.StringIO()):
                self.assertEqual(readings.main(["import", path], call=recorder.call), 0)
                self.assertEqual(readings.main(["import", path, "--apply"], call=recorder.call), 0)
        sent = recorder.calls[0][3]
        self.assertEqual(sent[0]["textSignature"], "t")
        self.assertEqual(sent[1]["recipeId"], 6)
        self.assertEqual([c[2]["apply"] for c in recorder.calls], ["false", "true"])

    def test_export_pages_until_the_last(self):
        recorder = Recorder([
            {"recipes": 1, "items": [{"recipeId": 1, "model": "phi4:latest"}], "next": 1},
            {"recipes": 1, "items": [{"recipeId": 2, "model": "phi4:latest"}], "next": None},
        ])
        with tempfile.TemporaryDirectory() as folder:
            out = os.path.join(folder, "out.jsonl")
            with redirect_stderr(io.StringIO()):
                self.assertEqual(readings.main(["export", "--out", out], call=recorder.call), 0)
            with open(out, encoding="utf-8") as handle:
                self.assertEqual([json.loads(l)["recipeId"] for l in handle], [1, 2])
        self.assertEqual([c[2]["after"] for c in recorder.calls], [0, 1])


if __name__ == "__main__":
    unittest.main()

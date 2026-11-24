"""
Tests for ops/source-rights-record.py: the TSV of forbidding hosts reads whole and within its column rules, a row
that breaks one stops the run before any call, the dry run and the apply send each row once with apply false or true,
a refusal (HTTP 409) is named and the run goes on with exit status 2, and --bodies calls nothing.
No network: nom-api is a recorded stand-in.
Run: python3 -m unittest discover -s ops/tests
"""

import contextlib
import importlib.util
import io
import json
import os
import sys
import tempfile
import unittest

OPS = os.path.join(os.path.dirname(__file__), "..")
_spec = importlib.util.spec_from_file_location("source_rights_record", os.path.join(OPS, "source-rights-record.py"))
record = importlib.util.module_from_spec(_spec)
sys.modules["source_rights_record"] = record
_spec.loader.exec_module(record)

HEADER = "host\tclass\tquote\tpage\tdate\n"
ROW = "www.example.org\tforbidden\tYou may not scrape this website.\thttps://www.example.org/terms\t2026-11-21\n"


def tsv(text: str) -> str:
    handle = tempfile.NamedTemporaryFile("w", suffix=".tsv", delete=False, encoding="utf-8")
    handle.write(text)
    handle.close()
    return handle.name


class FakeApi:
    def __init__(self, refuse: set | None = None, recorded: set | None = None):
        self.calls = []
        self.refuse = refuse or set()
        self.recorded = recorded or set()

    def post(self, path, params, body):
        self.calls.append((path, params, body))
        if body["host"] in self.refuse:
            raise record.attach.ApiError(409, json.dumps({"refused": "a class recorded by hand may be as strict or stricter, never laxer", "host": body["host"]}))
        applied = params["apply"] == "true"
        return {
            "host": body["host"].removeprefix("www."), "rightsClass": body["rightsClass"], "applied": applied and body["host"] not in self.recorded,
            "alreadyRecorded": body["host"] in self.recorded, "excluded": True, "sources": [{"domain": body["host"], "oldClass": None, "newClass": body["rightsClass"]}],
            "record": {"id": 5} if applied else None,
        }


class SourceRightsRecordTests(unittest.TestCase):
    def test_the_forbidding_hosts_file_reads_whole_within_its_column_rules(self):
        rows = record.load_rows(record.DEFAULT_TSV)
        self.assertEqual(
            sorted(r["host"].removeprefix("www.") for r in rows),
            ["budgetbytes.com", "davidlebovitz.com", "eatingwell.com", "pinchofyum.com", "sallysbakingaddiction.com", "seriouseats.com", "skinnytaste.com"])
        for row in rows:
            self.assertEqual(row["class"], "forbidden")
            self.assertLess(len(row["quote"].split()), 25, row["host"])
            self.assertTrue(row["page"].startswith("https://"))
        self.assertIn('"scrape,"', next(r["quote"] for r in rows if "budgetbytes" in r["host"]))

    def test_a_row_that_breaks_a_column_rule_stops_the_run_before_any_call(self):
        bad = [
            ROW.replace("www.example.org\t", "not a host\t", 1),
            ROW.replace("forbidden", "maybe"),
            ROW.replace("You may not scrape this website.", " ".join(["word"] * 25)),
            ROW.replace("https://www.example.org/terms", "terms page"),
            ROW.replace("2026-11-21", "21/11/2026"),
            ROW.replace("2026-11-21", "2999-01-01"),
        ]
        for line in bad:
            with self.subTest(line=line), self.assertRaises(SystemExit):
                record.load_rows(tsv(HEADER + line))
        with self.assertRaises(SystemExit):
            record.load_rows(tsv(HEADER + ROW + ROW.replace("www.example.org\t", "example.org\t", 1)))
        with self.assertRaises(SystemExit):
            record.load_rows(tsv("host\tclass\tquote\n" + ROW))

    def test_the_dry_run_and_the_apply_send_each_row_once_and_a_refusal_is_named_and_counted(self):
        rows = record.load_rows(tsv(HEADER + ROW + ROW.replace("www.example.org", "www.other.org")))
        api = FakeApi(refuse={"www.other.org"})
        out = io.StringIO()
        with contextlib.redirect_stdout(out):
            dry = record.run(rows, api.post, apply=False)
        self.assertEqual([c[1]["apply"] for c in api.calls], ["false", "false"])
        self.assertEqual(api.calls[0][0], "/api/ScrapingSource/rights/by-hand")
        self.assertEqual(api.calls[0][2], {"host": "www.example.org", "rightsClass": "forbidden", "quote": "You may not scrape this website.",
                                           "quoteUrl": "https://www.example.org/terms", "readOn": "2026-11-21"})
        self.assertEqual(dry["wouldRecord"], 1)
        self.assertEqual(dry["refused"], [{"host": "www.other.org", "reason": "a class recorded by hand may be as strict or stricter, never laxer"}])
        self.assertIn("example.org: forbidden; would be recorded", out.getvalue())
        self.assertIn("www.other.org: refused:", out.getvalue())

        api = FakeApi(recorded={"www.other.org"})
        with contextlib.redirect_stdout(io.StringIO()):
            applied = record.run(rows, api.post, apply=True)
        self.assertEqual([c[1]["apply"] for c in api.calls], ["true", "true"])
        self.assertEqual((applied["recorded"], applied["alreadyRecorded"], applied["refused"]), (1, 1, []))

    def test_an_error_other_than_a_refusal_stops_the_run(self):
        rows = record.load_rows(tsv(HEADER + ROW))

        def failing(path, params, body):
            raise record.attach.ApiError(500, "boom")

        with contextlib.redirect_stdout(io.StringIO()), self.assertRaises(record.attach.ApiError):
            record.run(rows, failing, apply=False)

    def test_bodies_call_nothing_and_a_refused_run_exits_2(self):
        out = io.StringIO()
        saved = os.environ.pop("NOM_API", None)
        try:
            with contextlib.redirect_stdout(out):
                self.assertEqual(record.main([tsv(HEADER + ROW), "--bodies"]), 0)
            self.assertEqual(json.loads(out.getvalue())[0]["host"], "www.example.org")
            with self.assertRaises(SystemExit):
                record.main([tsv(HEADER + ROW)])
        finally:
            if saved is not None:
                os.environ["NOM_API"] = saved


if __name__ == "__main__":
    unittest.main()

"""
Tests for the source era script: the year read from a Project Gutenberg text (header line, then
title page, only one year), the match of a text to its attribution, and what is sent to nom-api.
No network: nom-api is a recorded stand-in.
Run: python3 -m unittest discover -s ops/tests
"""

import importlib.util
import io
import os
import sys
import tempfile
import unittest
from contextlib import redirect_stderr, redirect_stdout

OPS = os.path.join(os.path.dirname(__file__), "..")
_spec = importlib.util.spec_from_file_location("source_era", os.path.join(OPS, "source-era.py"))
era = importlib.util.module_from_spec(_spec)
sys.modules["source_era"] = era
_spec.loader.exec_module(era)

HEADER = """The Project Gutenberg eBook of A Plain Cook Book

This ebook is for the use of anyone anywhere in the United States and most
other parts of the world at no cost. www.gutenberg.org

Title: A Plain Cook Book

Author: Mary Example

Release date: March 4, 2008 [eBook #24654]
                Most recently updated: January 3, 2021

Language: English

Credits: Produced by Example Volunteers, 2008

"""
START = "*** START OF THE PROJECT GUTENBERG EBOOK A PLAIN COOK BOOK ***\n"


class ReadYearTests(unittest.TestCase):
    def test_the_header_original_publication_line_wins(self):
        text = HEADER.replace("Language: English", "Original publication: Boston: Example & Co, 1896\n\nLanguage: English") + START + "A PLAIN COOK BOOK\n\n1901\n"
        reading = era.read_year(text, latest=2026)
        self.assertEqual((reading.year, reading.source), (1896, "pg-header:original-publication"))

    def test_one_year_on_the_title_page_is_read_and_the_ebook_dates_are_not(self):
        text = HEADER + START + "\n\nA PLAIN COOK BOOK\n\nBY MARY EXAMPLE\n\nBOSTON\nEXAMPLE AND COMPANY\n1901\n\nProduced by Example Volunteers in 2008\n"
        reading = era.read_year(text, latest=2026)
        self.assertEqual(reading.year, 1901)
        self.assertTrue(reading.source.startswith("title-page:line "))

    def test_two_years_or_none_leave_the_book_unknown(self):
        two = era.read_year(HEADER + START + "COOK BOOK\nCopyright 1911\nNew edition 1923\n", latest=2026)
        self.assertIsNone(two.year)
        self.assertEqual(two.source, "title-page: 1911, 1923")
        none = era.read_year(HEADER + START + "COOK BOOK\nBY A LADY\n", latest=2026)
        self.assertEqual((none.year, none.source), (None, "title-page: no year"))

    def test_a_year_after_the_title_page_or_out_of_range_is_not_read(self):
        later = START + "COOK BOOK\n" + "\n" * 250 + "In 1850 we baked bread.\n"
        self.assertIsNone(era.read_year(later, latest=2026).year)
        self.assertIsNone(era.read_year(START + "COOK BOOK\n1599\n", latest=2026).year)
        self.assertIsNone(era.read_year(START + "COOK BOOK\n2031\n", latest=2026).year)
        self.assertEqual(era.read_year("COOK BOOK\nPHILADELPHIA, 1857\n", latest=2026).year, 1857, "a text with no marker is read from its first line")

    def test_the_era_follows_the_year(self):
        self.assertEqual([era.era(1959), era.era(1960), era.era(None)], ["historic", "modern", "unknown"])


class PlanTests(unittest.TestCase):
    def setUp(self):
        self.dir = tempfile.mkdtemp()
        self.csv = os.path.join(self.dir, "books.csv")
        with open(self.csv, "w", encoding="utf-8") as handle:
            handle.write("file,attribution,book\n")
            handle.write("cookbook-24654.jsonl,A Plain Cook Book (Example 1901),A Plain Cook Book\n")
            handle.write("cookbook-6429.jsonl,First Edition (Example),\n")
            handle.write("cookbook-6429.jsonl,Second Edition (Example),\n")

    def write(self, name, text):
        path = os.path.join(self.dir, name)
        with open(path, "w", encoding="utf-8") as handle:
            handle.write(text)
        return path

    def test_a_text_takes_the_attribution_of_its_cookbook_file_and_two_editions_are_not_sent(self):
        files = [
            self.write("pg24654.txt", HEADER + START + "A PLAIN COOK BOOK\n1901\n"),
            self.write("pg6429.txt", START + "COOK BOOK\n1887\n"),
            self.write("pg999.txt", START + "COOK BOOK\n1890\n"),
        ]
        rows, report = era.plan(files, era.load_books(self.csv), latest=2026)
        self.assertEqual(rows, [{"sourceAttribution": "A Plain Cook Book (Example 1901)", "publishedYear": 1901, "yearSource": rows[0]["yearSource"]}])
        self.assertEqual([r["sent"] for r in report], ["yes", "no: the file holds more than one attribution", "no: no attribution in the CSV"])

    def test_main_prints_offline_and_sends_a_dry_run_or_an_apply_to_nom_api(self):
        path = self.write("pg24654.txt", HEADER + START + "A PLAIN COOK BOOK\n1901\n")
        calls = []

        def stand_in(path_, params, body=None):
            calls.append((path_, params, body))
            return {"applied": params["apply"] == "true", "stamp": "2026-11-21T10:00:00.000Z", "added": 1, "refused": []}

        with redirect_stdout(io.StringIO()) as out, redirect_stderr(io.StringIO()):
            self.assertEqual(era.main([path, "--attribution-csv", self.csv, "--api", ""]), 0)
        self.assertIn("A Plain Cook Book (Example 1901)\t1901\thistoric", out.getvalue())
        self.assertEqual(calls, [])

        with redirect_stdout(io.StringIO()), redirect_stderr(io.StringIO()):
            era.main([path, "--attribution-csv", self.csv], post=stand_in)
            era.main([path, "--attribution-csv", self.csv, "--apply"], post=stand_in)
            era.main(["--undo", "2026-11-21T10:00:00.000Z"], post=stand_in)
        self.assertEqual([(c[0], c[1]["apply"]) for c in calls], [
            (era.SET_PATH, "false"), (era.SET_PATH, "true"), (era.UNDO_PATH, "false")])
        self.assertEqual(calls[0][2][0]["publishedYear"], 1901)
        self.assertEqual(calls[2][1]["stamp"], "2026-11-21T10:00:00.000Z")


if __name__ == "__main__":
    unittest.main()

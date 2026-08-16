"""
Tests for the two catalog comparators.
Run: python3 -m unittest discover -s ops/tests

The important properties, in order:
  1. OFF output is ALWAYS a flag and NEVER carries an OFF value — that is the
     ODbL posture, and a regression here would quietly import crowd data as fact.
  2. FDC differences propose real USDA numbers with an authoritative source.
  3. Barcodes match across UPC-A / EAN-13 zero padding, or nothing matches at all.
"""

import csv
import importlib.util
import os
import subprocess
import sys
import tempfile
import unittest

OPS = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
sys.path.insert(0, OPS)


def _load(name: str, filename: str):
    spec = importlib.util.spec_from_file_location(name, os.path.join(OPS, filename))
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module


off = _load("offcmp", "food-catalog-off-compare.py")
fdc = _load("fdcdiff", "food-catalog-fdc-diff.py")

from catalog_compare import diff_row, to_float  # noqa: E402

CATALOG_HEADER = ["ingredient_id", "fdc_id", "gtin_upc", "source", "name", "food_group",
                  "is_whole_food", "reference_serving_grams", "kcal_per_100g",
                  "protein_per_100g", "carb_per_100g", "fat_per_100g"]


def write_csv(path: str, header: list[str], rows: list[list], delimiter: str = ",") -> None:
    with open(path, "w", newline="", encoding="utf-8") as fh:
        writer = csv.writer(fh, delimiter=delimiter)
        writer.writerow(header)
        writer.writerows(rows)


class BarcodeTests(unittest.TestCase):
    def test_upc_a_and_ean_13_match(self):
        # Same product: UPC-A on the pack, EAN-13 in a European database.
        self.assertEqual(off.normalize_barcode("012345678905"),
                         off.normalize_barcode("0012345678905"))

    def test_strips_non_digits(self):
        self.assertEqual(off.normalize_barcode(" 0-12345-67890-5 "), "12345678905")

    def test_rejects_too_short_or_empty(self):
        self.assertIsNone(off.normalize_barcode("1234"))
        self.assertIsNone(off.normalize_barcode(""))
        self.assertIsNone(off.normalize_barcode(None))

    def test_percent_difference(self):
        self.assertAlmostEqual(off.percent_difference(100, 50), 50.0)
        self.assertAlmostEqual(off.percent_difference(50, 50), 0.0)
        self.assertEqual(off.percent_difference(0, 0), 0.0)


class OffComparatorTests(unittest.TestCase):
    """End-to-end through the script, so the ODbL guarantees are tested as shipped."""

    def _run(self, catalog_rows, off_rows):
        with tempfile.TemporaryDirectory() as tmp:
            catalog = os.path.join(tmp, "catalog.csv")
            export = os.path.join(tmp, "off.csv")
            out = os.path.join(tmp, "flags.csv")
            write_csv(catalog, CATALOG_HEADER, catalog_rows)
            write_csv(export, ["code", "product_name", "energy-kcal_100g", "proteins_100g",
                               "carbohydrates_100g", "fat_100g"], off_rows, delimiter="\t")
            proc = subprocess.run(
                [sys.executable, os.path.join(OPS, "food-catalog-off-compare.py"),
                 catalog, export, "--out", out],
                capture_output=True, text=True)
            self.assertEqual(proc.returncode, 0, proc.stderr)
            with open(out, newline="", encoding="utf-8") as fh:
                return list(csv.DictReader(fh)), proc.stderr

    def test_disagreement_produces_a_flag_that_leaks_no_off_value(self):
        catalog = [["7", "123", "012345678905", "branded_food", "Bar", "Sweets/Snacks",
                    "true", "55", "436", "14.5", "54.5", "16.4"]]
        offrows = [["0012345678905", "Bar", "200", "5", "20", "3"]]  # very different
        proposals, _ = self._run(catalog, offrows)

        self.assertTrue(proposals)
        for p in proposals:
            self.assertEqual(p["action"], "flag")          # never an update
            self.assertEqual(p["field"], "")               # no nutrient field targeted
            self.assertEqual(p["proposed_value"], "")      # no OFF value copied
            self.assertTrue(p["source"].startswith("review:off/"))
            # The OFF numbers themselves must not appear in the text.
            for leaked in ("200", "20"):
                self.assertNotIn(f" {leaked} ", p["reason"])

    def test_agreement_produces_nothing(self):
        catalog = [["7", "123", "012345678905", "branded_food", "Bar", "", "", "",
                    "436", "14.5", "54.5", "16.4"]]
        offrows = [["0012345678905", "Bar", "440", "14.8", "55", "16.0"]]  # within tolerance
        proposals, _ = self._run(catalog, offrows)
        self.assertEqual(proposals, [])

    def test_unmatched_barcode_is_silent(self):
        catalog = [["7", "123", "012345678905", "branded_food", "Bar", "", "", "",
                    "436", "", "", ""]]
        offrows = [["999999999999", "Something else", "100", "1", "1", "1"]]
        proposals, stderr = self._run(catalog, offrows)
        self.assertEqual(proposals, [])
        self.assertIn("unmatched:         1", stderr)

    def test_value_we_lack_is_flagged_for_review(self):
        catalog = [["7", "123", "012345678905", "branded_food", "Bar", "", "", "",
                    "436", "", "", ""]]                     # no protein locally
        offrows = [["0012345678905", "Bar", "436", "14.5", "", ""]]
        proposals, _ = self._run(catalog, offrows)
        self.assertEqual(len(proposals), 1)
        self.assertEqual(proposals[0]["action"], "flag")
        self.assertIn("holds a value", proposals[0]["reason"])


class FdcDiffTests(unittest.TestCase):
    def test_split_csv_handles_quotes_and_escapes(self):
        self.assertEqual(fdc.split_csv('"a","b,c","d""e"'), ["a", "b,c", 'd"e'])

    def test_energy_falls_back_to_atwater_factors(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = os.path.join(tmp, "food_nutrient.csv")
            with open(path, "w", encoding="utf-8") as fh:
                fh.write("id,fdc_id,nutrient_id,amount\n")
                fh.write('"1","100","2048","250"\n')   # Atwater specific only
                fh.write('"2","100","1003","8"\n')
                fh.write('"3","200","1008","99"\n')    # general energy present
                fh.write('"4","200","2047","55"\n')
            result = fdc.read_current_nutrients(path, {"100", "200"})
        self.assertEqual(result["100"]["kcal_per_100g"], 250.0)   # fell back
        self.assertEqual(result["100"]["protein_per_100g"], 8.0)
        self.assertEqual(result["200"]["kcal_per_100g"], 99.0)    # preferred general

    def test_diff_row_reports_only_real_differences(self):
        ours = {"kcal_per_100g": "100", "protein_per_100g": "10",
                "carb_per_100g": "", "fat_per_100g": "5"}
        theirs = {"kcal_per_100g": 100.5, "protein_per_100g": 30.0,
                  "carb_per_100g": 12.0, "fat_per_100g": None}
        columns = {d.column for d in diff_row(ours, theirs)}
        self.assertEqual(columns, {"protein_per_100g", "carb_per_100g"})

    def test_to_float_tolerates_blanks(self):
        self.assertIsNone(to_float(""))
        self.assertIsNone(to_float(None))
        self.assertIsNone(to_float("n/a"))
        self.assertEqual(to_float("12.5"), 12.5)


if __name__ == "__main__":
    unittest.main()

"""
Tests for the cross-checker's corroboration and safety rules.
Run: python3 -m unittest discover -s ops/tests
"""

import importlib.util
import os
import sys
import unittest

OPS = os.path.join(os.path.dirname(__file__), "..")
sys.path.insert(0, OPS)

_spec = importlib.util.spec_from_file_location("cc", os.path.join(OPS, "food-catalog-crosscheck.py"))
cc = importlib.util.module_from_spec(_spec)
sys.modules["cc"] = cc  # dataclasses need the module registered
_spec.loader.exec_module(cc)

from nutrition_normalize import normalize  # noqa: E402

# 240 kcal in a 55 g bar -> 436.4 kcal/100 g
BAR = normalize(serving_size="1 bar (55 g)", kcal="240", protein="8 g", carb="30 g", fat="9 g")


def reading(host: str, nutrition=BAR):
    return cc.SourceReading(url=f"https://{host}/p", host=host, nutrition=nutrition)


class JsonLdTests(unittest.TestCase):
    def test_extracts_and_mass_normalizes(self):
        html = ('<script type="application/ld+json">{"@type":"NutritionInformation",'
                '"servingSize":"1 bar (55 g)","calories":"240 calories","proteinContent":"8 g"}'
                '</script>')
        result = cc.extract_jsonld_nutrition(html)
        self.assertAlmostEqual(result.kcal, 436.36, places=1)
        self.assertAlmostEqual(result.serving_grams, 55.0)

    def test_finds_nutrition_inside_graph(self):
        html = ('<script type="application/ld+json">{"@graph":[{"@type":"Recipe","nutrition":'
                '{"@type":"NutritionInformation","servingSize":"100 g","calories":"52"}}]}'
                '</script>')
        self.assertAlmostEqual(cc.extract_jsonld_nutrition(html).kcal, 52.0)

    def test_ignores_pages_without_nutrition(self):
        self.assertIsNone(cc.extract_jsonld_nutrition('<script type="application/ld+json">'
                                                      '{"@type":"Product","name":"x"}</script>'))
        self.assertIsNone(cc.extract_jsonld_nutrition("<html>no structured data</html>"))


class CorroborationTests(unittest.TestCase):
    def setUp(self):
        self.row = {"ingredient_id": "7", "fdc_id": "123", "name": "Bar",
                    "kcal_per_100g": "200", "protein_per_100g": "",
                    "carb_per_100g": "", "fat_per_100g": ""}

    def test_two_agreeing_hosts_propose_a_numeric_update(self):
        proposals = cc.compare(self.row, [reading("a.com"), reading("b.com")])
        kcal = [p for p in proposals if p["field"] == "kcal_per_100g"]
        self.assertEqual(len(kcal), 1)
        self.assertEqual(kcal[0]["action"], "update")
        self.assertAlmostEqual(float(kcal[0]["proposed_value"]), 436.36, places=1)
        # Numeric changes must carry an authoritative source prefix (ProposalPolicy).
        self.assertTrue(kcal[0]["source"].startswith("label:"))

    def test_single_source_can_only_flag(self):
        proposals = cc.compare(self.row, [reading("a.com")])
        self.assertTrue(proposals)
        for p in proposals:
            self.assertEqual(p["action"], "flag")
            self.assertEqual(p["field"], "")           # flags never target a nutrient field
            self.assertTrue(p["source"].startswith("review:"))

    def test_same_host_twice_is_not_corroboration(self):
        # Two pages on one site are one source, not two.
        proposals = cc.compare(self.row, [reading("a.com"), reading("a.com")])
        self.assertTrue(all(p["action"] == "flag" for p in proposals))

    def test_agreement_with_catalog_produces_nothing(self):
        row = dict(self.row, kcal_per_100g="436", protein_per_100g="14.5",
                   carb_per_100g="54.5", fat_per_100g="16.4")
        self.assertEqual(cc.compare(row, [reading("a.com"), reading("b.com")]), [])

    def test_volume_derived_serving_cannot_drive_a_numeric_change(self):
        # Density was assumed, so even corroborated readings only earn a flag.
        drink = normalize(serving_size="1 cup (240 ml)", kcal="120")
        proposals = cc.compare(self.row, [reading("a.com", drink), reading("b.com", drink)])
        self.assertTrue(proposals)
        for p in proposals:
            self.assertEqual(p["action"], "flag")
            self.assertIn("density", p["reason"])

    def test_disagreeing_sources_flag_rather_than_pick_one(self):
        other = normalize(serving_size="55 g", kcal="90")   # wildly different
        proposals = cc.compare(self.row, [reading("a.com"), reading("b.com", other)])
        kcal = [p for p in proposals if "kcal" in p["reason"]]
        self.assertTrue(all(p["action"] == "flag" for p in kcal))


class FetcherTests(unittest.TestCase):
    def test_allow_list_is_enforced(self):
        fetcher = cc.PoliteFetcher({"example.com"})
        self.assertTrue(fetcher.is_allowed_host("https://example.com/a"))
        self.assertTrue(fetcher.is_allowed_host("https://www.example.com/a"))
        self.assertTrue(fetcher.is_allowed_host("https://shop.example.com/a"))
        self.assertFalse(fetcher.is_allowed_host("https://evil.com/a"))
        self.assertFalse(fetcher.is_allowed_host("https://notexample.com/a"))

    def test_non_allowlisted_host_is_refused_before_any_request(self):
        ok, why = cc.PoliteFetcher({"example.com"}).can_fetch("https://elsewhere.com/x")
        self.assertFalse(ok)
        self.assertEqual(why, "host_not_allowlisted")


if __name__ == "__main__":
    unittest.main()

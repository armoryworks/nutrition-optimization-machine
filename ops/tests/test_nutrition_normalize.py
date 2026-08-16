"""
Tests for mass normalization. Run: python3 -m unittest discover -s ops/tests

The scaling is the load-bearing part of the cross-checker: labels are per serving,
the catalog is per 100 g, and an unscaled comparison would flag nearly everything.
"""

import os
import sys
import unittest

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))

from nutrition_normalize import (  # noqa: E402
    agrees,
    is_per_100g,
    normalize,
    parse_quantity,
    parse_serving_mass,
    scale_to_100g,
)


class ParseServingMassTests(unittest.TestCase):
    def test_plain_grams(self):
        self.assertAlmostEqual(parse_serving_mass("55 g").grams, 55.0)
        self.assertAlmostEqual(parse_serving_mass("55g").grams, 55.0)

    def test_parenthesised_mass_wins_over_count(self):
        # "1 bar" is a count, not a mass; the real serving mass is in parentheses.
        self.assertAlmostEqual(parse_serving_mass("1 bar (55 g)").grams, 55.0)
        self.assertAlmostEqual(parse_serving_mass("2 tbsp (32g)").grams, 32.0)

    def test_ounces_convert(self):
        self.assertAlmostEqual(parse_serving_mass("1.5 oz").grams, 42.524, places=2)

    def test_volume_marks_assumed_density(self):
        mass = parse_serving_mass("1 cup (240 ml)")
        self.assertAlmostEqual(mass.grams, 240.0)
        self.assertTrue(mass.assumed_density)

    def test_countable_serving_without_mass_is_refused(self):
        self.assertIsNone(parse_serving_mass("1 bar"))
        self.assertIsNone(parse_serving_mass("about 12 chips"))
        self.assertIsNone(parse_serving_mass(""))
        self.assertIsNone(parse_serving_mass(None))

    def test_implausible_masses_refused(self):
        self.assertIsNone(parse_serving_mass("0.01 g"))
        self.assertIsNone(parse_serving_mass("50 lb"))


class ParseQuantityTests(unittest.TestCase):
    def test_reads_leading_number(self):
        self.assertEqual(parse_quantity("240 calories"), 240.0)
        self.assertEqual(parse_quantity("8 g"), 8.0)
        self.assertEqual(parse_quantity("0.5g"), 0.5)
        self.assertEqual(parse_quantity(12), 12.0)

    def test_handles_less_than_and_commas(self):
        self.assertEqual(parse_quantity("<1 g"), 1.0)
        self.assertEqual(parse_quantity("1,5 g"), 1.5)

    def test_no_number_is_none(self):
        self.assertIsNone(parse_quantity("trace"))
        self.assertIsNone(parse_quantity(None))


class NormalizeTests(unittest.TestCase):
    def test_scales_bar_to_100g(self):
        # 240 kcal per 55 g bar -> 436.4 kcal/100 g. Getting this wrong is the whole point.
        result = normalize(serving_size="1 bar (55 g)", kcal="240 calories",
                           protein="8 g", carb="30 g", fat="9 g")
        self.assertAlmostEqual(result.kcal, 436.36, places=1)
        self.assertAlmostEqual(result.protein_g, 14.55, places=1)
        self.assertAlmostEqual(result.carb_g, 54.55, places=1)
        self.assertAlmostEqual(result.fat_g, 16.36, places=1)
        self.assertAlmostEqual(result.serving_grams, 55.0)

    def test_passes_through_when_already_per_100g(self):
        result = normalize(serving_size="100 g", kcal="52", protein="0.3", carb="14", fat="0.2")
        self.assertAlmostEqual(result.kcal, 52.0)
        self.assertAlmostEqual(result.serving_grams, 100.0)

    def test_larger_serving_scales_down(self):
        # A 240 g yogurt cup at 150 kcal is 62.5 kcal/100 g.
        result = normalize(serving_size="1 cup (240 g)", kcal="150")
        self.assertAlmostEqual(result.kcal, 62.5)

    def test_refuses_without_serving_mass(self):
        self.assertIsNone(normalize(serving_size="1 bar", kcal="240"))
        self.assertIsNone(normalize(serving_size=None, kcal="240"))

    def test_returns_none_when_no_values(self):
        self.assertIsNone(normalize(serving_size="55 g"))

    def test_flags_density_assumption(self):
        result = normalize(serving_size="1 cup (240 ml)", kcal="120")
        self.assertTrue(result.assumed_density)
        self.assertTrue(any("density" in n for n in result.notes))

    def test_partial_panels_scale_what_is_present(self):
        result = normalize(serving_size="28 g", kcal="160", fat="14 g")
        self.assertAlmostEqual(result.kcal, 571.43, places=1)
        self.assertAlmostEqual(result.fat_g, 50.0)
        self.assertIsNone(result.protein_g)


class ScaleAndAgreeTests(unittest.TestCase):
    def test_scale_to_100g(self):
        self.assertAlmostEqual(scale_to_100g(240, 55), 436.36, places=1)
        self.assertIsNone(scale_to_100g(None, 55))
        self.assertIsNone(scale_to_100g(240, 0))

    def test_is_per_100g(self):
        self.assertTrue(is_per_100g("100 g"))
        self.assertTrue(is_per_100g("per 100g"))
        self.assertFalse(is_per_100g("55 g"))
        self.assertFalse(is_per_100g(None))

    def test_agreement_tolerates_label_rounding(self):
        self.assertTrue(agrees(436.4, 440.0))     # within 15%
        self.assertTrue(agrees(0.4, 0.5))         # absolute floor
        self.assertFalse(agrees(436.4, 52.0))     # genuinely different
        self.assertFalse(agrees(None, 52.0))


if __name__ == "__main__":
    unittest.main()

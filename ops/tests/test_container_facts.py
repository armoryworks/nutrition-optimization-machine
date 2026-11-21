"""
Tests for ops/golden/container_facts.py, the reader written apart from the C# import that
freezes container-facts.tsv and checks package-weights.tsv.
Run: python3 -m unittest discover -s ops/tests
"""

import collections
import importlib.util
import os
import tempfile
import unittest

OPS = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")


def _load():
    spec = importlib.util.spec_from_file_location("container_facts", os.path.join(OPS, "golden", "container_facts.py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


cf = _load()


class PackageWeightTests(unittest.TestCase):
    def test_a_gram_segment_wins_over_ounces(self):
        self.assertEqual(cf.package_weight("15 oz/425 g"), (425.0, None))
        self.assertEqual(cf.package_weight("16 oz/1 lbs/454 g"), (454.0, None))

    def test_gs1_codes_are_read(self):
        self.assertEqual(cf.package_weight("1.25 LBR"), (567.0, None))
        self.assertEqual(cf.package_weight("64 OZA"), (None, 1892.7))
        self.assertEqual(cf.package_weight("170 GRM"), (170.0, None))

    def test_volume_and_weight_are_kept_apart(self):
        self.assertEqual(cf.package_weight("16 fl oz/1 PT/473 mL"), (None, 473.0))
        self.assertEqual(cf.package_weight("25.4 oz/750 mL"), (720.1, 750.0))

    def test_multi_packs_counts_and_unread_strings_give_nothing(self):
        for text in ["4 x 5.3 oz", "12 ct", "6 pk", "1 EA", "5 H87", ""]:
            self.assertIsNone(cf.package_weight(text), text)


class ClassScanTests(unittest.TestCase):
    def test_one_current_us_product_per_gtin_counts_and_excluded_words_do_not(self):
        head = ('"fdc_id","brand_owner","brand_name","subbrand_name","gtin_upc","ingredients","not_a_significant_source_of","serving_size",'
                '"serving_size_unit","household_serving_fulltext","branded_food_category","data_source","package_weight","modified_date",'
                '"available_date","market_country","discontinued_date","preparation_state_code","trade_channel","short_description","material_code"')

        def row(i, gtin, weight, available, country="United States", discontinued=""):
            return (f'"{i}","","","","{gtin}","","","","","","Canned & Bottled Beans","","{weight}","","{available}","{country}",'
                    f'"{discontinued}","","","",""')

        with tempfile.TemporaryDirectory() as d:
            with open(os.path.join(d, "branded_food.csv"), "w", encoding="utf-8") as f:
                f.write("\n".join([head, row(1, "0001", "15 oz/425 g", "2020-01-01"), row(2, "0001", "15.5 oz/439 g", "2024-01-01"),
                                   row(3, "0002", "15 oz/425 g", "2024-01-01", country="Canada"), row(4, "0003", "16 oz/454 g", "2024-01-01")]) + "\n")
            with open(os.path.join(d, "food.csv"), "w", encoding="utf-8") as f:
                f.write('"fdc_id","data_type","description","food_category_id","publication_date"\n'
                        '"1","branded_food","BLACK BEANS","",""\n"2","branded_food","BLACK BEANS","",""\n'
                        '"3","branded_food","BLACK BEANS","",""\n"4","branded_food","REFRIED BLACK BEANS","",""\n')
            classes = [{"id": "canned-black-beans", "basis": "g", "brandedCategories": "Canned & Bottled Beans",
                        "productAll": "black,beans", "productNone": "refried"}]
            sizes = cf.scan(d, classes)
            self.assertEqual(sizes["canned-black-beans"], collections.Counter({439: 1}))


if __name__ == "__main__":
    unittest.main()

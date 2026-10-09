"""
Tests for the recipe image finder's licence policy, source parsing and triangulation.
No network: source responses are recorded fixtures and model answers are given directly.
Run: python3 -m unittest discover -s ops/tests
"""

import importlib.util
import os
import sys
import unittest

OPS = os.path.join(os.path.dirname(__file__), "..")
sys.path.insert(0, OPS)

import image_sources as src  # noqa: E402
import image_triangulation as tri  # noqa: E402

_spec = importlib.util.spec_from_file_location("finder", os.path.join(OPS, "recipe-image-finder.py"))
finder = importlib.util.module_from_spec(_spec)
sys.modules["finder"] = finder
_spec.loader.exec_module(finder)

SOUP = tri.RecipeText(
    recipe_id=109,
    name="Creamy Tomato Basil Soup",
    description="A velvety tomato soup with fresh basil and a swirl of cream.",
    ingredients=["2 tbsp olive oil", "1 onion, diced", "28 oz crushed tomatoes", "1/2 cup heavy cream",
                 "1/4 cup fresh basil leaves"],
    steps=["Saute the onion.", "Add tomatoes and simmer.", "Blend, stir in cream and basil, and serve."],
)

GOOD_ANSWERS = {"is_photograph": "yes", "finished_dish": "yes", "raw_ingredients_only": "no",
                "packaging_or_product": "no", "people_prominent": "no", "text_or_watermark": "no",
                "collage": "no", "dish_matches": "yes", "visible_ingredients": ["tomato soup", "basil", "cream"]}

WIKIMEDIA_FIXTURE = {"query": {"pages": [
    {"title": "File:Tomato basil soup.jpg", "imageinfo": [{
        "mime": "image/jpeg", "thumburl": "https://upload.wikimedia.org/thumb/640px-Tomato_basil_soup.jpg",
        "descriptionurl": "https://commons.wikimedia.org/wiki/File:Tomato_basil_soup.jpg",
        "extmetadata": {
            "License": {"value": "cc-by-4.0"}, "LicenseShortName": {"value": "CC BY 4.0"},
            "LicenseUrl": {"value": "https://creativecommons.org/licenses/by/4.0"},
            "Artist": {"value": '<a href="//commons.wikimedia.org/wiki/User:Jane">Jane Cook</a>'},
            "ImageDescription": {"value": "<p>Bowl of tomato soup with <b>basil</b></p>"},
            "Categories": {"value": "Tomato soups|Basil"}}}]},
    {"title": "File:Soup share-alike.jpg", "imageinfo": [{
        "mime": "image/jpeg", "thumburl": "https://upload.wikimedia.org/x.jpg", "descriptionurl": "https://c/x",
        "extmetadata": {"License": {"value": "cc-by-sa-4.0"}}}]},
    {"title": "File:Branded soup can.jpg", "imageinfo": [{
        "mime": "image/jpeg", "thumburl": "https://upload.wikimedia.org/y.jpg", "descriptionurl": "https://c/y",
        "extmetadata": {"License": {"value": "cc0"}, "Restrictions": {"value": "trademarked"}}}]},
    {"title": "File:Soup diagram.svg", "imageinfo": [{"mime": "image/svg+xml", "extmetadata": {}}]},
]}}

OPENVERSE_FIXTURE = {"results": [
    {"id": "d544", "title": "My Own Chicken Tikka Masala", "creator": "Tobyotter",
     "creator_url": "https://www.flickr.com/photos/78428166@N00", "license": "by", "license_version": "2.0",
     "license_url": "https://creativecommons.org/licenses/by/2.0/",
     "foreign_landing_url": "https://www.flickr.com/photos/78428166@N00/5580275871",
     "url": "https://live.staticflickr.com/5022/5580275871_9593593900_b.jpg",
     "thumbnail": "https://api.openverse.org/v1/images/d544/thumb/", "source": "flickr", "mature": False,
     "tags": [{"name": "chicken"}, {"name": "rice"}]},
    {"id": "m1", "title": "Mature", "license": "by", "mature": True},
]}

UNSPLASH_FIXTURE = {"results": [{
    "id": "abc", "alt_description": "tomato soup in a white bowl", "description": None,
    "urls": {"small": "https://images.unsplash.com/photo-1?w=400", "regular": "https://images.unsplash.com/photo-1?w=1080"},
    "links": {"html": "https://unsplash.com/photos/abc", "download_location": "https://api.unsplash.com/photos/abc/download"},
    "user": {"name": "Sam Lens", "links": {"html": "https://unsplash.com/@sam"}}}]}


class LicenceTests(unittest.TestCase):
    def test_spellings_map_to_families(self):
        cases = {
            "cc-by-4.0": "by", "CC BY 2.0": "by", "cc-by-sa-3.0": "by-sa", "by-nd": "by-nd", "cc0": "cc0",
            "CC0 1.0": "cc0", "pdm": "pd", "Public domain": "pd", "PD-self": "pd", "cc-by-nc-2.0": "nc",
            "by-nc-sa": "nc", "GFDL": "gfdl", "": "unknown", "All rights reserved": "other",
        }
        for raw, family in cases.items():
            self.assertEqual(src.normalize_licence(raw), family, raw)

    def test_share_alike_is_opt_in_and_non_commercial_never(self):
        self.assertNotIn("by-sa", src.DEFAULT_ALLOWED)
        self.assertIn("by-sa", src.resolve_allowed(["by-sa"]))
        for forbidden in ("by-nc", "gfdl"):
            with self.assertRaises(ValueError):
                src.resolve_allowed([forbidden])
        self.assertFalse(src.licence_allowed("nc", {"nc"}))

    def test_openverse_is_asked_only_for_allowed_licences(self):
        param = src.openverse_licence_param(src.DEFAULT_ALLOWED)
        self.assertEqual(set(param.split(",")), {"cc0", "pdm", "by", "by-nd"})


class SourceParsingTests(unittest.TestCase):
    def test_wikimedia_keeps_credit_and_drops_restricted_and_vector_files(self):
        found = src.parse_wikimedia(WIKIMEDIA_FIXTURE, "tomato basil soup")
        titles = [c.title for c in found]
        self.assertEqual(titles, ["Tomato basil soup", "Soup share-alike"])
        soup = found[0]
        self.assertEqual(soup.licence, "by")
        self.assertEqual(soup.author, "Jane Cook")
        self.assertEqual(soup.author_url, "https://commons.wikimedia.org/wiki/User:Jane")
        self.assertEqual(soup.description, "Bowl of tomato soup with basil")
        self.assertFalse(soup.hotlink)
        self.assertEqual(found[1].licence, "by-sa")

    def test_openverse_skips_mature_results_and_labels_the_licence(self):
        found = src.parse_openverse(OPENVERSE_FIXTURE, "q")
        self.assertEqual(len(found), 1)
        self.assertEqual(found[0].licence_label, "CC BY 2.0")
        self.assertEqual(found[0].provider, "flickr")
        self.assertEqual(found[0].key, "openverse:d544")

    def test_unsplash_is_hotlinked_with_referral_links_and_download_tracking(self):
        photo = src.parse_unsplash(UNSPLASH_FIXTURE, "q", "nom")[0]
        self.assertTrue(photo.hotlink)
        self.assertTrue(photo.full_url.startswith("https://images.unsplash.com/"))
        self.assertIn("utm_source=nom", photo.author_url)
        self.assertIn("utm_medium=referral", photo.landing_url)
        self.assertEqual(photo.download_location, "https://api.unsplash.com/photos/abc/download")

    def test_pexels_and_pixabay_titles_come_from_alt_text_and_slugs(self):
        pexels = src.parse_pexels({"photos": [{"id": 7, "alt": "Bowl of soup", "url": "https://www.pexels.com/photo/bowl-of-tomato-soup-7/",
                                               "photographer": "P", "src": {"medium": "m", "large2x": "l"}}]}, "q")[0]
        self.assertEqual(pexels.tags, ["bowl", "of", "tomato", "soup"])
        pixabay = src.parse_pixabay({"hits": [{"id": 9, "pageURL": "https://pixabay.com/photos/soup-tomato-9/",
                                               "tags": "soup, tomato", "user": "u", "user_id": 3,
                                               "webformatURL": "w", "largeImageURL": "L"}]}, "q")[0]
        self.assertEqual(pixabay.title, "soup tomato")
        self.assertEqual(pixabay.author_url, "https://pixabay.com/users/u-3/")


class TextTests(unittest.TestCase):
    def test_name_tokens_drop_marketing_noise(self):
        self.assertEqual(tri.name_key_tokens("The BEST Easy Homemade Creamy Tomato Basil Soup!"), ["tomato", "basil", "soup"])
        self.assertEqual(tri.name_key_tokens("Chewy Chocolate Chip Cookies"), tri.name_key_tokens("chewy chocolate chip cookie"))
        self.assertEqual(tri.clean_dish_name("Gluten-Free Low Carb Banana Bread"), "banana bread")

    def test_ingredient_heads_skip_staples(self):
        self.assertEqual(tri.ingredient_heads(SOUP.ingredients), ["onion", "tomato", "cream", "basil"])

    def test_queries_go_from_specific_to_general(self):
        recipe = tri.RecipeText(1, "Grandma's Snickerdoodles", dish_group="Sugar cookies",
                                ingredients=["2 cups flour", "1 cup butter", "2 tsp cinnamon"])
        self.assertEqual(tri.build_queries(recipe), ["snickerdoodles", "sugar cookies", "snickerdoodles butter"])

    def test_name_coverage_counts_plurals_and_joined_tags(self):
        self.assertEqual(tri.name_coverage("Tomato Basil Soup", "tomatoes basil soups"), 1.0)
        self.assertEqual(tri.name_coverage("Chicken Tikka Masala", "chickentikkamasala food"), 1.0)
        self.assertAlmostEqual(tri.name_coverage("Chicken Tikka Masala", "a bowl of curry with chicken"), 1 / 3)


def evidence(key, meta_cov=1.0, meta_sim=0.8, caption_sim=0.75, answers=None, rank=9.0, recipe=SOUP,
             caption="A bowl of tomato soup garnished with basil."):
    t = tri.Thresholds()
    meta_text = "tomato basil soup" if meta_cov >= 1 else "soup"
    e = tri.CandidateEvidence(key=key)
    e.metadata = tri.metadata_check(recipe, meta_text, meta_sim, t)
    e.caption = tri.caption_check(caption, {"name": caption_sim, "instructions": caption_sim - 0.1}, t)
    e.vqa, e.content_ok, e.content_reasons = tri.vqa_check(recipe, answers or GOOD_ANSWERS, t)
    e.rank_score = rank
    return e


class TriangulationTests(unittest.TestCase):
    def setUp(self):
        self.t = tri.Thresholds()

    def test_all_four_checks_passing_attaches(self):
        a = evidence("a", rank=9)
        a.pairwise = {"b": [True, True]}
        decision = tri.decide({"a": a, "b": evidence("b", caption_sim=0.3, rank=4)}, self.t)
        self.assertEqual(decision.outcome, "attach")
        self.assertEqual(decision.chosen, "a")
        self.assertEqual(decision.per_candidate["a"]["checks_passed"], 4)

    def test_a_lone_candidate_cannot_show_a_margin(self):
        decision = tri.decide({"a": evidence("a", rank=9)}, self.t)
        self.assertEqual(decision.outcome, "review")
        self.assertIn("lone candidate", " ".join(decision.per_candidate["a"]["ranking"]["reasons"]))
        self.assertEqual(tri.decide({"a": evidence("a", rank=9)}, tri.Thresholds(rank_min_rivals=0)).outcome, "attach")
        self.assertEqual(tri.decide({"a": evidence("a", rank=5)}, tri.Thresholds(rank_min_rivals=0)).outcome, "review")

    def test_failed_metadata_downgrades_to_review(self):
        decision = tri.decide({"a": evidence("a", meta_cov=0.0, meta_sim=0.2)}, self.t)
        self.assertEqual(decision.outcome, "review")
        self.assertIn("metadata", decision.reasons[0])

    def test_second_model_preferring_another_photo_blocks_attach(self):
        a = evidence("a", rank=9)
        a.pairwise = {"b": [False, False]}
        decision = tri.decide({"a": a, "b": evidence("b", meta_cov=0.0, meta_sim=0.2, rank=9)}, self.t)
        self.assertEqual(decision.outcome, "review")
        self.assertIn("not preferred over b", " ".join(decision.per_candidate["a"]["ranking"]["reasons"]))

    def test_a_split_vote_is_not_a_margin(self):
        a = evidence("a", rank=9)
        a.pairwise = {"b": [True, False]}
        decision = tri.decide({"a": a, "b": evidence("b", caption_sim=0.2, rank=9)}, self.t)
        self.assertEqual(decision.outcome, "review")
        self.assertIn("won 50%", " ".join(decision.per_candidate["a"]["ranking"]["reasons"]))
        self.assertEqual(tri.decide({"a": a, "b": evidence("b", caption_sim=0.2, rank=9)},
                                    tri.Thresholds(rank_win_share=0.5)).outcome, "attach")

    def test_an_uncompared_rival_blocks_attach(self):
        decision = tri.decide({"a": evidence("a", rank=9), "b": evidence("b", caption_sim=0.2, rank=9)}, self.t)
        self.assertEqual(decision.outcome, "review")
        self.assertIn("not compared", " ".join(decision.per_candidate["a"]["ranking"]["reasons"]))

    def test_rejected_pictures_are_not_rivals(self):
        a = evidence("a", rank=9)
        a.pairwise = {"c": [True, True]}
        junk = evidence("b", answers=dict(GOOD_ANSWERS, text_or_watermark="yes"), rank=10)
        decision = tri.decide({"a": a, "b": junk, "c": evidence("c", caption_sim=0.2)}, self.t)
        self.assertEqual(decision.outcome, "attach")
        self.assertEqual(decision.per_candidate["a"]["ranking"]["rivals"], 1)

    def test_round_robin_covers_every_eligible_pair_and_votes_are_mirrored(self):
        junk = evidence("c", answers=dict(GOOD_ANSWERS, collage="yes"))
        dup = evidence("d")
        dup.duplicate_of = "a"
        ev = {"a": evidence("a"), "b": evidence("b"), "c": junk, "d": dup, "e": evidence("e")}
        self.assertEqual(tri.round_robin(ev), [("a", "b"), ("a", "e"), ("b", "e")])
        tri.record_vote(ev, "a", "b", "A")
        tri.record_vote(ev, "b", "a", "same")
        self.assertEqual(ev["a"].pairwise["b"], [True, False])
        self.assertEqual(ev["b"].pairwise["a"], [False, False])

    def test_one_vote_is_not_a_comparison_in_both_orders(self):
        a = evidence("a", rank=9)
        a.pairwise = {"b": [True]}
        decision = tri.decide({"a": a, "b": evidence("b", caption_sim=0.2, rank=9)}, self.t)
        self.assertIn("not compared", " ".join(decision.per_candidate["a"]["ranking"]["reasons"]))

    def test_a_duplicate_of_the_chosen_photo_does_not_eat_the_margin(self):
        dup = evidence("b", rank=9)
        dup.duplicate_of = "a"
        a = evidence("a", rank=9)
        a.pairwise = {"c": [True, True]}
        decision = tri.decide({"a": a, "b": dup, "c": evidence("c", caption_sim=0.2)}, self.t)
        self.assertEqual(decision.outcome, "attach")
        self.assertEqual(decision.chosen, "a")

    def test_content_gate_failure_is_never_queued(self):
        for flag in ("text_or_watermark", "people_prominent", "collage", "packaging_or_product", "raw_ingredients_only"):
            answers = dict(GOOD_ANSWERS, **{flag: "yes"})
            decision = tri.decide({"a": evidence("a", answers=answers)}, self.t)
            self.assertEqual(decision.outcome, "none", flag)

    def test_not_a_finished_dish_is_disqualified(self):
        decision = tri.decide({"a": evidence("a", answers=dict(GOOD_ANSWERS, finished_dish="no"))}, self.t)
        self.assertEqual(decision.outcome, "none")

    def test_blind_caption_about_another_dish_fails_check_b(self):
        e = evidence("a", caption_sim=0.41, caption="A stack of pancakes with syrup.")
        self.assertFalse(e.caption.passed)
        self.assertIn("not about this recipe", e.caption.reasons[0])

    def test_caption_mentioning_a_watermark_fails_check_b(self):
        e = evidence("a", caption="A bowl of tomato soup with a watermark logo in the corner.")
        self.assertFalse(e.caption.passed)

    def test_ingredient_overlap_needs_recipe_ingredients(self):
        answers = dict(GOOD_ANSWERS, visible_ingredients=["shrimp", "rice", "lime"])
        result, content_ok, _ = tri.vqa_check(SOUP, answers, self.t)
        self.assertTrue(content_ok)
        self.assertFalse(result.passed)
        self.assertEqual(result.detail["ingredient_overlap"], 0.0)

    def test_garnishes_named_in_the_steps_count_as_recipe_foods(self):
        pancakes = tri.RecipeText(1, "Classic Buttermilk Pancakes", ingredients=["2 cups flour", "2 eggs", "1 cup buttermilk"],
                                  steps=["Serve warm with maple syrup and blueberries if desired."])
        answers = dict(GOOD_ANSWERS, visible_ingredients=["pancakes", "blueberries", "maple syrup", "bacon"])
        result, _, _ = tri.vqa_check(pancakes, answers, self.t)
        self.assertEqual(result.detail["matched_ingredients"], ["pancakes", "blueberries", "maple syrup"])
        self.assertTrue(result.passed)

    def test_dish_mismatch_fails_vqa_but_not_the_content_gate(self):
        result, content_ok, _ = tri.vqa_check(SOUP, dict(GOOD_ANSWERS, dish_matches="no"), self.t)
        self.assertTrue(content_ok)
        self.assertFalse(result.passed)

    def test_one_passing_check_is_not_worth_a_humans_time(self):
        decision = tri.decide({"a": evidence("a", meta_cov=0.0, meta_sim=0.1, caption_sim=0.2,
                                             answers=dict(GOOD_ANSWERS, dish_matches="no"), rank=9)}, self.t)
        self.assertEqual(decision.outcome, "none")

    def test_review_lists_at_most_max_review_candidates_best_first(self):
        ev = {k: evidence(k, meta_cov=0.0, meta_sim=0.2, rank=r) for k, r in (("a", 6), ("b", 5), ("c", 4), ("d", 3))}
        decision = tri.decide(ev, self.t, max_review=2)
        self.assertEqual(decision.outcome, "review")
        self.assertEqual(decision.review, ["a", "b"])

    def test_thresholds_are_configurable(self):
        strict = tri.Thresholds(rank_min_score=9.5)
        self.assertEqual(tri.decide({"a": evidence("a", rank=9)}, strict).outcome, "review")

    def test_every_candidate_is_explained(self):
        decision = tri.decide({"a": evidence("a"), "b": evidence("b", answers=dict(GOOD_ANSWERS, collage="yes"))}, self.t)
        self.assertEqual(set(decision.per_candidate), {"a", "b"})
        self.assertEqual(decision.per_candidate["b"]["content_reasons"], ["a collage or multiple panels"])
        for name in ("metadata", "caption", "vqa"):
            self.assertIn("passed", decision.per_candidate["a"][name])


class DuplicateTests(unittest.TestCase):
    def test_near_identical_hashes_collapse_to_the_preferred_key(self):
        dupes = tri.mark_duplicates({"cc0": 0b1010, "by": 0b1011, "other": 0xFFFF_0000_FFFF_0000}, ["cc0", "by", "other"])
        self.assertEqual(dupes, {"by": "cc0"})

    def test_missing_hashes_are_never_duplicates(self):
        self.assertEqual(tri.mark_duplicates({"a": None, "b": None}, ["a", "b"]), {})


class FinderGlueTests(unittest.TestCase):
    def test_model_replies_with_prose_still_parse(self):
        self.assertEqual(finder.parse_json_object('Sure! {"score": 8, "reason": "soup"} hope that helps'),
                         {"score": 8, "reason": "soup"})
        self.assertEqual(finder.parse_json_object("no json here"), {})

    def test_payload_carries_credit_and_evidence_but_bytes_only_when_rehosted(self):
        commons = src.parse_wikimedia(WIKIMEDIA_FIXTURE, "q")[0]
        decision = tri.decide({commons.key: evidence(commons.key)}, tri.Thresholds())
        payload = finder.candidate_payload(commons, decision, "attached", (b"jpeg", "image/jpeg"))
        self.assertEqual(payload["licenseCode"], "by")
        self.assertEqual(payload["author"], "Jane Cook")
        self.assertEqual(payload["imageBase64"], "anBlZw==")
        self.assertIn("metadata", payload["checks"])

        unsplash = src.parse_unsplash(UNSPLASH_FIXTURE, "q", "nom")[0]
        hot = finder.candidate_payload(unsplash, decision, "pending", None)
        self.assertTrue(hot["hotlink"])
        self.assertNotIn("imageBase64", hot)

    def test_unsplash_stays_off_until_ai_use_is_confirmed(self):
        os.environ["UNSPLASH_ACCESS_KEY"] = "k"
        try:
            off = finder.UnsplashSource(finder.Http(None), src.DEFAULT_ALLOWED, "nom", ai_confirmed=False)
            on = finder.UnsplashSource(finder.Http(None), src.DEFAULT_ALLOWED, "nom", ai_confirmed=True)
        finally:
            del os.environ["UNSPLASH_ACCESS_KEY"]
        self.assertFalse(off.enabled())
        self.assertIn("unsplash.com/data", off.disabled_reason)
        self.assertTrue(on.enabled())

    def test_keyless_sources_are_skipped_not_failed(self):
        for name in ("PEXELS_API_KEY", "PIXABAY_API_KEY"):
            os.environ.pop(name, None)
        self.assertFalse(finder.PexelsSource(finder.Http(None), src.DEFAULT_ALLOWED).enabled())
        self.assertFalse(finder.PixabaySource(finder.Http(None), src.DEFAULT_ALLOWED).enabled())

    def test_unsplash_download_ping_only_goes_to_the_unsplash_api(self):
        os.environ["UNSPLASH_ACCESS_KEY"] = "k"
        try:
            source = finder.UnsplashSource(finder.Http(None), src.DEFAULT_ALLOWED, "nom", ai_confirmed=True)
        finally:
            del os.environ["UNSPLASH_ACCESS_KEY"]
        self.assertFalse(source.track_download("https://evil.example/ping"))


if __name__ == "__main__":
    unittest.main()

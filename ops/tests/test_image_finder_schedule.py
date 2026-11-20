"""
Tests for running the image finder by the clock: the paged work list, the time limit and
waiting out nom-api's 429 answers. No network: nom-api is a fake and sleeping is recorded.
Run: python3 -m unittest discover -s ops/tests
"""

import email.message
import importlib.util
import io
import os
import sys
import unittest
import urllib.error
from datetime import datetime, timezone

OPS = os.path.join(os.path.dirname(__file__), "..")
sys.path.insert(0, OPS)

if "finder" not in sys.modules:
    _spec = importlib.util.spec_from_file_location("finder", os.path.join(OPS, "recipe-image-finder.py"))
    sys.modules["finder"] = importlib.util.module_from_spec(_spec)
    _spec.loader.exec_module(sys.modules["finder"])
finder = sys.modules["finder"]


class FakeMissing:
    """GET /api/RecipeImages/missing over a list of recipe ids, clamped at 500 like the API; searched ones drop out."""

    def __init__(self, ids, marks_searched=True):
        self.waiting = list(ids)
        self.marks_searched = marks_searched
        self.asked: list[int] = []

    def __call__(self, count):
        self.asked.append(count)
        return [{"id": i, "name": f"Recipe {i}"} for i in self.waiting[: min(count, 500)]]

    def searched(self, recipe_id):
        if self.marks_searched:
            self.waiting.remove(recipe_id)


def drain(api, limit, page_size):
    handed = []
    for row in finder.work_list(api, limit, page_size):
        handed.append(row["id"])
        api.searched(row["id"])
    return handed


class WorkListTests(unittest.TestCase):
    def test_a_run_goes_past_the_apis_500_per_call(self):
        api = FakeMissing(range(1, 1301))
        self.assertEqual(drain(api, 1200, 500), list(range(1, 1201)))
        self.assertEqual(api.asked, [500, 500, 200])

    def test_pages_never_ask_for_more_than_the_api_gives(self):
        api = FakeMissing(range(1, 2001))
        drain(api, 2000, 5000)
        self.assertTrue(all(n <= 500 for n in api.asked))

    def test_the_list_ends_when_the_backlog_does(self):
        api = FakeMissing(range(1, 151))
        self.assertEqual(drain(api, 1000, 100), list(range(1, 151)))

    def test_a_page_of_recipes_already_handed_out_ends_the_run(self):
        api = FakeMissing(range(1, 301), marks_searched=False)
        self.assertEqual(drain(api, 1000, 100), list(range(1, 101)))
        self.assertEqual(api.asked, [100, 100])

    def test_a_recipe_left_unsearched_is_not_handed_out_twice(self):
        api = FakeMissing(range(1, 11))
        handed = []
        for row in finder.work_list(api, 10, 4):
            handed.append(row["id"])
            if row["id"] != 2:
                api.searched(row["id"])
        self.assertEqual(handed, list(range(1, 11)))

    def test_a_failed_fetch_ends_the_list(self):
        self.assertEqual(list(finder.work_list(lambda n: [], 100, 50)), [])
        self.assertEqual(list(finder.work_list(lambda n: None, 100, 50)), [])


class TimeLimitTests(unittest.TestCase):
    def test_no_limit_by_default(self):
        self.assertFalse(finder.out_of_time(0.0, 0, 10 ** 9))

    def test_the_run_stops_starting_recipes_once_the_minutes_are_up(self):
        self.assertFalse(finder.out_of_time(100.0, 450, 100.0 + 449 * 60))
        self.assertTrue(finder.out_of_time(100.0, 450, 100.0 + 450 * 60))


def http_error(code, retry_after=None):
    headers = email.message.Message()
    if retry_after is not None:
        headers["Retry-After"] = retry_after
    return urllib.error.HTTPError("https://nom.example/api/RecipeImages/missing", code, "error", headers, io.BytesIO(b""))


class ScriptedApi(finder.NomApi):
    def __init__(self, answers, **kwargs):
        self.slept: list[float] = []
        super().__init__("https://nom.example", "k", None, sleep=self.slept.append, **kwargs)
        self.answers = list(answers)
        self.calls = 0

    def _open(self, req):
        self.calls += 1
        answer = self.answers.pop(0)
        if isinstance(answer, Exception):
            raise answer
        return answer


class RetryAfterTests(unittest.TestCase):
    def test_a_429_is_waited_out_for_as_long_as_the_api_asks(self):
        api = ScriptedApi([http_error(429, "7"), [{"id": 1}]])
        self.assertEqual(api.missing(10), [{"id": 1}])
        self.assertEqual(api.slept, [7.0])
        self.assertEqual(api.rate_limited, 1)

    def test_an_http_date_is_understood(self):
        now = datetime(2026, 11, 20, 8, 0, 0, tzinfo=timezone.utc)
        self.assertEqual(finder.retry_after_seconds("Fri, 20 Nov 2026 08:02:00 GMT", 60, now), 120.0)
        self.assertEqual(finder.retry_after_seconds("Fri, 20 Nov 2026 07:00:00 GMT", 60, now), 0.0)

    def test_a_missing_or_unreadable_header_waits_the_default(self):
        self.assertEqual(finder.retry_after_seconds(None, 60), 60)
        self.assertEqual(finder.retry_after_seconds("soon", 60), 60)
        api = ScriptedApi([http_error(429), None])
        api.searched([1])
        self.assertEqual(api.slept, [60])

    def test_a_wait_longer_than_the_limit_is_not_slept_through(self):
        api = ScriptedApi([http_error(429, "86400")], max_wait=900)
        with self.assertRaises(urllib.error.HTTPError):
            api.missing(10)
        self.assertEqual(api.slept, [])

    def test_retries_are_bounded(self):
        api = ScriptedApi([http_error(429, "1")] * 5, max_retries=3)
        with self.assertRaises(urllib.error.HTTPError):
            api.submit({"recipeId": 1, "candidates": []})
        self.assertEqual(api.calls, 4)
        self.assertEqual(api.slept, [1.0, 1.0, 1.0])

    def test_other_errors_are_not_retried(self):
        api = ScriptedApi([http_error(500, "5")])
        with self.assertRaises(urllib.error.HTTPError):
            api.missing(10)
        self.assertEqual((api.calls, api.slept), (1, []))



class SubmitOnly:
    """POST /api/RecipeImages/candidates answering with fixed outcomes; records what was submitted."""

    def __init__(self, *outcomes):
        self.outcomes = list(outcomes)
        self.submitted: list[dict] = []

    def submit(self, payload):
        self.submitted.append(payload)
        return {"recipeId": payload["recipeId"], "outcomes": [{"outcome": o} for o in self.outcomes]}


class QueueCapTests(unittest.TestCase):
    def settle(self, api, outcome, payloads=({"sourceKey": "a"},)):
        marked = []
        result = finder.settle(api, 7, outcome, list(payloads), "image-finder-20261120", marked.append)
        return result, marked

    def test_a_review_held_whole_leaves_the_recipe_unsearched_and_reports_the_queue_full(self):
        (held, full), marked = self.settle(SubmitOnly("held", "held"), "review")
        self.assertEqual((held, full, marked), (2, True, []))

    def test_a_review_with_one_candidate_stored_marks_the_recipe_searched(self):
        (held, full), marked = self.settle(SubmitOnly("pending", "held"), "review")
        self.assertEqual((held, full, marked), (1, False, [7]))

    def test_a_review_with_nothing_held_marks_the_recipe_searched(self):
        (held, full), marked = self.settle(SubmitOnly("pending", "duplicate"), "review")
        self.assertEqual((held, full, marked), (0, False, [7]))

    def test_an_attach_marks_the_recipe_searched_before_it_submits(self):
        api = SubmitOnly("attached")
        (held, full), marked = self.settle(api, "attach")
        self.assertEqual((held, full, marked, len(api.submitted)), (0, False, [7], 1))

    def test_a_review_with_no_image_to_send_is_marked_searched_and_submits_nothing(self):
        api = SubmitOnly()
        (held, full), marked = self.settle(api, "review", payloads=())
        self.assertEqual((held, full, marked, api.submitted), (0, False, [7], []))

    def test_queue_full_needs_a_held_outcome_and_nothing_stored(self):
        self.assertTrue(finder.queue_full({"outcomes": [{"outcome": "held"}, {"outcome": "rejected"}]}))
        self.assertFalse(finder.queue_full({"outcomes": [{"outcome": "rejected"}]}))
        self.assertFalse(finder.queue_full({"outcomes": [{"outcome": "held"}, {"outcome": "pending"}]}))
        self.assertFalse(finder.queue_full(None))


if __name__ == "__main__":
    unittest.main()

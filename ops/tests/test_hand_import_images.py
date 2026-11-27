"""
Tests for the Library of Congress page images (the tile.loc.gov page-image determination of 2026-11-25 in
docs/architecture/crosscheck-sources.md): the images step refuses everything outside its limits, one test per limit,
the text step deletes an image once the page stores its reading, and the page image is read for its tables row by
row with tesseract's word boxes and confidence. The page images are made for the tests (table-pages.py), from NCI's
U.S. government text set in the layout of the Library's old cookbooks; nothing is fetched from the Library. Their
tesseract readings (tesseract 5.3.4, --psm 11, TSV) are fixtures, so the reading is tested without tesseract; one test
reads the images themselves where tesseract is installed (in the nom-hand-import image).
Run: python3 -m unittest discover -s ops/tests
"""

import contextlib
import dataclasses
import importlib.util
import io
import json
import os
import shutil
import sys
import tempfile
import unittest

OPS = os.path.join(os.path.dirname(__file__), "..")
REPO = os.path.join(OPS, "..")
_spec = importlib.util.spec_from_file_location("hand_import_images", os.path.join(OPS, "hand-import.py"))
hand = importlib.util.module_from_spec(_spec)
sys.modules["hand_import_images"] = hand
_spec.loader.exec_module(hand)
cookbook = hand.cookbook

FIXTURES = os.path.join(os.path.dirname(__file__), "fixtures", "hand-import")
WORD_LIST = os.path.join(FIXTURES, "words.txt")
WORDS = cookbook.load_words([WORD_LIST])
SOURCES = hand.load_sources()
with open(os.path.join(FIXTURES, "loc-book.text.json"), encoding="utf-8") as _handle:
    LOC_TEXT = json.load(_handle)
with open(os.path.join(FIXTURES, "table-pages.json"), encoding="utf-8") as _handle:
    TABLE_PAGES = json.load(_handle)["pages"]

JPEG = b"\xff\xd8\xff\xe0" + b"\0" * 64 + b"\xff\xd9"
RIGHTS = ["<p>The books in this collection are in the public domain and are free to use and reuse.</p>"]
BOOK = hand.Book("loc-b", "loc", "loc-item", "https://www.loc.gov/item/b/", "book", "B", "Library of Congress. B")


def item(rights=None, date="1891", language=("english",), pages=3, image_host="tile.loc.gov"):
    return {"item": {"rights_advisory": RIGHTS if rights is None else rights, "date": date, "language": list(language)},
            "resources": [{"url": "https://www.loc.gov/resource/gdc.b/",
                           "fulltext_derivative": "https://tile.loc.gov/storage-services/public/gdc/b/b.text.json",
                           "files": [[{"mimetype": "image/jpeg", "url": f"https://{image_host}/b/{n:04d}.jpg", "width": 2000}]
                                     for n in range(1, pages + 1)]}]}


def library_text():
    """Page 1 the Library's garbled ingredient table (fails the readability test), page 2 a readable page, page 3
    another garbled table."""
    return {"1": LOC_TEXT["31"], "2": LOC_TEXT["40"], "3": LOC_TEXT["19"]}


class Site:
    """A stand-in for the Library's hosts: robots.txt answers as given, an image URL answers a JPEG (or what it is told)."""

    def __init__(self, robots=None, answers=None):
        self.calls = []
        self.robots = robots or {}
        self.answers = answers or {}

    def __call__(self, url, headers):
        self.calls.append(url)
        host = hand.host_of(url)
        if url.endswith("/robots.txt"):
            status, body = self.robots.get(host, (404, ""))
            return hand.Response(status, {}, iter([body.encode()]))
        if url in self.answers:
            return self.answers[url]
        return hand.Response(200, {"content-type": "image/jpeg", "content-length": str(len(JPEG))}, iter([JPEG]))

    def fetched(self):
        return [c for c in self.calls if not c.endswith("/robots.txt")]


class Clock:
    def __init__(self):
        self.now = 0.0
        self.slept = []

    def __call__(self):
        return self.now

    def sleep(self, seconds):
        self.slept.append(seconds)
        self.now += seconds


class ImageLimitTests(unittest.TestCase):
    def setUp(self):
        self.folder = tempfile.mkdtemp()
        self.addCleanup(shutil.rmtree, self.folder)
        self.store = hand.Store(os.path.join(self.folder, "work"))
        self.text_dir = os.path.join(self.store.out, "text")
        os.makedirs(self.text_dir)
        self.site = Site()
        self.clock = Clock()

    def fetcher(self, sources=None):
        fetcher = hand.fetcher_for(sources or SOURCES)
        fetcher.opener, fetcher.clock, fetcher.sleep = self.site, self.clock, self.clock.sleep
        return fetcher

    def add(self, name, data):
        with open(self.store.path(name), "wb") as handle:
            handle.write(data)
        self.store.add(name, {"size": len(data), "sha256": hand.sha256_file(self.store.path(name)), "type": "json"})

    def taken(self, record=None, text=None, pages_json=True):
        self.add("loc-b.item.json", json.dumps(record or item()).encode())
        if text is not False:
            self.add("loc-b.text.json", json.dumps(text or library_text()).encode())
        if pages_json:
            doc = hand.text_document(BOOK, self.store, words=WORDS) if text is not False else {"pages": []}
            self.write_pages(doc)

    def write_pages(self, doc):
        with open(os.path.join(self.text_dir, "loc-b.pages.json"), "w", encoding="utf-8") as handle:
            json.dump(doc, handle)

    def images(self, sources=None, words=WORDS):
        return hand.images([BOOK], sources or SOURCES, self.store, self.fetcher(sources), self.text_dir, words=words)

    def test_the_library_page_images_are_determined_for_tile_loc_gov_only(self):
        self.assertEqual({k: s.image_hosts for k, s in SOURCES.items()}, {"va": (), "nhlbi": (), "loc": ("tile.loc.gov",), "nci": ()})
        with open(os.path.join(REPO, "docs", "architecture", "crosscheck-sources.md"), encoding="utf-8") as handle:
            rows = {line.split("|")[1].strip(" `"): line for line in handle if line.startswith("| `")}
        for source in SOURCES.values():
            for host in source.image_hosts:
                self.assertIn(host, source.hosts)
                self.assertIn("page image", rows[host], f"{host} is an image host with no determination that covers page images")

    def test_only_the_failing_pages_of_a_taken_item_are_fetched_from_tile_loc_gov(self):
        self.taken()
        report = self.images()
        self.assertEqual(report["counts"], {"fetched": 2})
        self.assertEqual(self.site.fetched(), ["https://tile.loc.gov/b/0001.jpg", "https://tile.loc.gov/b/0003.jpg"])
        self.assertEqual(sorted(hand.page_images(BOOK, self.store)), [1, 3])

    def test_an_item_whose_record_and_text_are_not_taken_is_refused(self):
        self.taken(text=False)
        report = self.images()
        self.assertEqual(report["counts"], {"refused": 1})
        self.assertIn("the item is not taken", report["refused"][0]["reason"])
        self.assertEqual(self.site.calls, [])

    def test_an_item_whose_rights_do_not_state_the_public_domain_is_refused(self):
        self.taken(item(rights=["Rights status not evaluated."]))
        report = self.images()
        self.assertEqual(report["refused"], [{"doc": "loc-b", "reason": "the item is not taken under the Library rule: its rights field "
                                                                       "does not state the public domain"}])
        self.assertEqual(self.site.calls, [])

    def test_an_item_dated_after_1930_is_refused(self):
        self.taken(item(date="1931"))
        report = self.images()
        self.assertIn("not dated 1930 or earlier (1931)", report["refused"][0]["reason"])
        self.assertEqual(self.site.calls, [])

    def test_an_item_not_in_english_is_refused(self):
        self.taken(item(language=("german",)))
        report = self.images()
        self.assertIn("not in English", report["refused"][0]["reason"])
        self.assertEqual(self.site.calls, [])

    def test_without_a_word_list_no_page_is_fetched(self):
        self.taken()
        report = self.images(words=None)
        self.assertIn("no word list", report["refused"][0]["reason"])
        self.assertEqual(self.site.calls, [])

    def test_a_page_whose_library_text_reads_is_refused_whatever_its_pages_file_says(self):
        self.taken()
        with open(os.path.join(self.text_dir, "loc-b.pages.json"), encoding="utf-8") as handle:
            doc = json.load(handle)
        for page in doc["pages"]:
            page["needsImage"] = True
        self.write_pages(doc)
        report = self.images()
        self.assertEqual(report["counts"], {"fetched": 2, "refused": 1})
        self.assertEqual(report["refused"], [{"doc": "loc-b", "page": 2, "reason": "its Library text passes the readability test"}])
        self.assertNotIn("https://tile.loc.gov/b/0002.jpg", self.site.calls)

    def test_an_image_on_a_host_without_a_page_image_determination_is_refused(self):
        self.add("loc-b.item.json", json.dumps(item(image_host="www.loc.gov")).encode())
        self.add("loc-b.text.json", json.dumps(library_text()).encode())
        self.write_pages(hand.text_document(BOOK, self.store, words=WORDS))
        report = self.images()
        self.assertEqual(report["counts"], {"refused": 2}, "www.loc.gov is a host of the source, but no determination covers its images")
        self.assertIn("www.loc.gov has no determination", report["refused"][0]["reason"])
        undetermined = {**SOURCES, "loc": dataclasses.replace(SOURCES["loc"], image_hosts=())}
        self.taken()
        self.assertEqual(self.images(undetermined)["counts"], {"refused": 2})
        self.assertEqual(self.site.calls, [])

    def test_a_redirect_off_the_image_host_is_refused(self):
        self.taken()
        self.site.answers["https://tile.loc.gov/b/0001.jpg"] = hand.Response(302, {}, iter(()), "https://www.loc.gov/b/0001.jpg")
        report = self.images()
        self.assertEqual(report["counts"], {"fetched": 1, "refused": 1})
        self.assertIn("www.loc.gov is not a host of this document's source", report["refused"][0]["reason"])
        self.assertNotIn("https://www.loc.gov/b/0001.jpg", self.site.calls)

    def test_robots_txt_as_read_in_the_run_is_honoured(self):
        self.taken()
        self.site.robots["tile.loc.gov"] = (200, "User-agent: *\nDisallow: /b/0003.jpg\n")
        report = self.images()
        self.assertEqual(report["counts"], {"fetched": 1, "refused": 1})
        self.assertIn("disallowed by robots.txt", report["refused"][0]["reason"])
        self.assertEqual(self.site.fetched(), ["https://tile.loc.gov/b/0001.jpg"])

    def test_robots_txt_that_cannot_be_read_stops_the_host_for_the_run(self):
        self.taken()
        self.site.robots["tile.loc.gov"] = (503, "")
        report = self.images()
        self.assertEqual(report["counts"], {"refused": 2})
        self.assertIn("robots.txt of tile.loc.gov could not be read", report["refused"][0]["reason"])
        self.assertEqual(self.site.fetched(), [])

    def test_the_images_share_the_librarys_pacing_with_www_loc_gov_and_its_crawl_delay(self):
        self.taken()
        fetcher = self.fetcher()
        self.site.robots["www.loc.gov"] = (200, "User-agent: *\nCrawl-Delay: 5\n")
        fetcher.read("https://www.loc.gov/item/b/?fo=json")
        start = len(self.clock.slept)
        hand.images([BOOK], SOURCES, self.store, fetcher, self.text_dir, words=WORDS)
        self.assertEqual(self.clock.slept[start:], [6.0, 6.0, 6.0], "robots.txt and two images, each 6 s after the request before, "
                                                                   "www.loc.gov's included")
        self.site.robots["tile.loc.gov"] = (200, "User-agent: *\nCrawl-delay: 20\n")
        for name in list(self.store.entries):
            if name.endswith(".jpg"):
                self.store.delete(name, "test")
        slow = self.fetcher()
        self.clock.slept.clear()
        hand.images([BOOK], SOURCES, self.store, slow, self.text_dir, words=WORDS)
        self.assertEqual(self.clock.slept, [20.0, 20.0], "a longer crawl-delay read on the day is kept")

    def test_an_answer_that_is_not_an_image_is_refused_and_not_kept(self):
        self.taken()
        page = b"<!doctype html><html><body>moved</body></html>"
        self.site.answers["https://tile.loc.gov/b/0001.jpg"] = hand.Response(200, {"content-type": "text/html", "content-length": str(len(page))},
                                                                              iter([page]))
        report = self.images()
        self.assertIn("expected a page image, got html", report["refused"][0]["reason"])
        self.assertFalse(os.path.exists(self.store.path("loc-b.p0001.jpg")))
        self.assertEqual(sorted(hand.page_images(BOOK, self.store)), [3])

    def test_part_of_an_image_is_never_kept(self):
        self.taken()
        self.site.answers["https://tile.loc.gov/b/0001.jpg"] = hand.Response(200, {"content-type": "image/jpeg", "content-length": "600"},
                                                                              iter([JPEG[:40]]))
        self.site.answers["https://tile.loc.gov/b/0003.jpg"] = hand.Response(200, {"content-type": "image/jpeg"}, iter([JPEG[:-2]]))
        report = self.images()
        self.assertEqual(report["counts"], {"refused": 2})
        self.assertIn("40 bytes of 600; the partial file is deleted", report["refused"][0]["reason"])
        self.assertIn("no end marker", report["refused"][1]["reason"])
        self.assertEqual([n for n in os.listdir(os.path.join(self.store.out, "docs")) if ".p0" in n], [])

        def dropped():
            yield JPEG[:40]
            raise OSError("connection reset")
        self.site.answers["https://tile.loc.gov/b/0001.jpg"] = hand.Response(200, {"content-type": "image/jpeg"}, dropped())
        with self.assertRaises(OSError):
            self.images()
        self.assertFalse(os.path.exists(self.store.path("loc-b.p0001.jpg.part")))

    def test_part_of_an_image_left_by_a_run_that_stopped_is_deleted_whatever_its_book(self):
        self.taken()
        for name in ("loc-gone.p0031.jpg.part", "loc-b.p0002.jp2.part"):
            with open(self.store.path(name), "wb") as handle:
                handle.write(JPEG[:40])
        with open(self.store.path("loc-big.pdf.part"), "wb") as handle:
            handle.write(b"%PDF-1.4")
        self.assertEqual(self.images()["counts"], {"fetched": 2, "partialDeleted": 2})
        self.assertEqual(sorted(n for n in os.listdir(os.path.join(self.store.out, "docs")) if n.endswith(".part")), ["loc-big.pdf.part"],
                         "a document's part is still resumed; only page images are never kept in part")
        with open(self.store.path("loc-gone.p0031.jpg.part"), "wb") as handle:
            handle.write(JPEG[:40])
        self.books = os.path.join(self.folder, "books.csv")
        with open(self.books, "w", encoding="utf-8") as handle:
            handle.write("doc_id,source,kind,url,layout,title,attribution\n")
            handle.write(",".join(dataclasses.astuple(BOOK)) + "\n")
        real = hand.tesseract_image
        self.addCleanup(setattr, hand, "tesseract_image", real)
        hand.tesseract_image = lambda path, runner=None, words=None: self.TABLE
        _, counts, _ = self.text("--words", WORD_LIST)
        self.assertEqual(counts["partialImagesDeleted"], 1)
        self.assertFalse(os.path.exists(self.store.path("loc-gone.p0031.jpg.part")))

    TABLE = "CORN BREAD.\n\nCorn meal . . . . . . 1 pint\nFlour . . . . . . 1 pint\nMilk . . . . . . 1 pint\n\nMix and bake in a quick oven.\n"

    def read_images(self):
        self.taken()
        self.images()
        self.books = os.path.join(self.folder, "books.csv")
        with open(self.books, "w", encoding="utf-8") as handle:
            handle.write("doc_id,source,kind,url,layout,title,attribution\n")
            handle.write(",".join(dataclasses.astuple(BOOK)) + "\n")
        read = []
        real = hand.tesseract_image
        self.addCleanup(setattr, hand, "tesseract_image", real)
        hand.tesseract_image = lambda path, runner=None, words=None: read.append(os.path.basename(path)) or (self.TABLE if path.endswith("0001.jpg") else None)
        return read

    def text(self, *words):
        out = io.StringIO()
        with contextlib.redirect_stdout(out), contextlib.redirect_stderr(io.StringIO()):
            code = hand.main(["text", "--out", self.store.out, "--books", self.books, *words])
        with open(os.path.join(self.text_dir, "loc-b.pages.json"), encoding="utf-8") as handle:
            return code, json.loads(out.getvalue()), {p["n"]: p for p in json.load(handle)["pages"]}

    def test_a_read_image_is_deleted_once_its_page_stores_the_text_and_never_fetched_again(self):
        read = self.read_images()
        table = self.TABLE

        def text():
            return self.text("--words", WORD_LIST)

        code, counts, pages = text()
        self.assertEqual(read, ["loc-b.p0001.jpg", "loc-b.p0003.jpg"])
        self.assertEqual(code, hand.STOPPED, "page 3's image was not read here (no tesseract): kept for a run in the image")
        self.assertEqual(counts["imagesDeleted"], 1)
        self.assertEqual((pages[1]["method"], pages[1]["imageText"]), ("loc-image-ocr", table))
        self.assertFalse(os.path.exists(self.store.path("loc-b.p0001.jpg")), "the image is deleted once the page stores its text")
        self.assertTrue(os.path.exists(self.store.path("loc-b.p0003.jpg")))
        with open(os.path.join(self.store.out, "SHA256SUMS"), encoding="utf-8") as handle:
            self.assertNotIn("p0001", handle.read())
        with open(self.store.manifest_path, encoding="utf-8") as handle:
            last = [json.loads(line) for line in handle][-1]
        self.assertEqual((last["file"], last["deleted"]), ("loc-b.p0001.jpg", True))
        self.assertNotIn("loc-b.p0001.jpg", hand.Store(self.store.out).entries, "a later run never takes the deleted image as fetched")

        read.clear()
        code, counts, pages = text()
        self.assertEqual(read, ["loc-b.p0003.jpg"], "page 1 keeps its stored reading with no image to read")
        self.assertEqual((pages[1]["method"], pages[1]["text"]), ("loc-image-ocr", table))
        self.site.calls.clear()
        report = self.images()
        self.assertEqual(report["counts"], {"alreadyRead": 1, "verified": 1})
        self.assertEqual(self.site.calls, [], "a page whose image was read is never fetched again")

    def test_a_stored_reading_outlives_a_text_run_without_the_word_list_or_with_another(self):
        self.read_images()
        self.text("--words", WORD_LIST)
        self.assertFalse(os.path.exists(self.store.path("loc-b.p0001.jpg")))
        self.addCleanup(setattr, cookbook, "WORD_LISTS", cookbook.WORD_LISTS)
        cookbook.WORD_LISTS = ()
        _, _, pages = self.text()
        self.assertEqual((pages[1]["method"], pages[1]["text"], pages[1]["imageText"]), ("loc-image-ocr", self.TABLE, self.TABLE),
                         "with no word list the page keeps the earlier step's choice and its stored reading")
        self.site.calls.clear()
        self.assertEqual(self.images()["counts"], {"alreadyRead": 1, "verified": 1})
        self.assertEqual(self.site.calls, [], "the page whose image was read is not fetched again")
        with open(os.path.join(self.text_dir, "loc-b.pages.json"), encoding="utf-8") as handle:
            earlier = {p["n"]: p for p in json.load(handle)["pages"]}
        with open(self.store.path("loc-b.text.json"), encoding="utf-8") as handle:
            data = json.load(handle)
        data["1"] = data["2"]
        with open(self.store.path("loc-b.text.json"), "w", encoding="utf-8") as handle:
            json.dump(data, handle)
        pages, _ = hand.loc_pages(self.store.path("loc-b.text.json"), WORDS, {}, lambda p: None, earlier)
        self.assertEqual((pages[0]["method"], pages[0]["needsImage"], pages[0]["imageText"]), ("loc-ocr", False, self.TABLE),
                         "a page that passes under another word list keeps its stored reading beside the Library's text")


def word(left, text, conf=96.0, top=100, height=40):
    return hand.Word(left, top, max(20, 25 * len(text)), height, conf, text)


class TableRowTests(unittest.TestCase):
    EDGE, MIDDLE, HEIGHT = 250, 1250, 40

    def row(self, *words):
        return hand.table_row(list(words), self.HEIGHT, self.EDGE, self.MIDDLE, dictionary=WORDS)

    def test_a_row_read_with_confidence_is_name_leaders_amount(self):
        self.assertEqual(self.row(word(310, "Chili"), word(450, "powder"), word(1770, "2"), word(1810, "teaspoons")),
                         "Chili powder . . . . . . 2 teaspoons")
        self.assertEqual(self.row(word(310, "Cornmeal"), word(560, "eee", 12), word(700, "©", 80), word(1770, "2"), word(1810, "tablespoons")),
                         "Cornmeal . . . . . . 2 tablespoons", "the leaders read as stray letters are dropped")

    def test_an_amount_below_the_bar_or_a_fraction_read_as_a_sign_is_unread(self):
        for amount in ([word(1770, "4", 48), word(1810, "teaspoon")], [word(1770, "1%", 93), word(1830, "lbs.")],
                       [word(1770, "%", 51), word(1810, "cup")], [word(1770, "6"), word(1800, "2"), word(1830, "cups")]):
            self.assertEqual(self.row(word(310, "Salt"), word(500, "ee", 10), *amount), f"Salt . . . . . . {cookbook.UNREAD}",
                             [w.text for w in amount])

    def test_a_name_cut_by_the_leaders_is_unread(self):
        self.assertEqual(self.row(word(310, "Plain"), word(450, "dried"), word(600, "bread"), word(760, "crumbs........0++", 30),
                                  word(1770, "2"), word(1810, "cups")), f"{cookbook.UNREAD} . . . . . . {cookbook.UNREAD}")

    def test_prose_and_a_centred_title_are_no_table_rows(self):
        prose = [word(310 + 150 * i, w) for i, w in enumerate("Mix the meal and flour, add the eggs well beaten and the milk".split())]
        self.assertIsNone(self.row(*prose))
        prose[4] = dataclasses.replace(prose[4], conf=40.0)
        self.assertIsNone(self.row(*prose), "one doubtful word in prose")
        self.assertIsNone(self.row(word(970, "BLACK"), word(1170, "SKILLET"), word(1410, "BEFF.", 84)))

    def test_a_row_whose_amount_was_not_read_at_all_gets_the_mark_between_table_rows(self):
        words = [word(310, "Beef", top=100), word(450, "broth", top=100), word(1770, "2", top=100), word(1810, "cups", top=100),
                 word(310, "Red-skinned", top=175), word(640, "potatoes,", top=175), word(890, "halved", top=175),
                 word(310, "Mixed", top=250), word(460, "vegetables", top=250), word(1770, "3", top=250), word(1810, "cups", top=250),
                 word(250, "Mix", top=400), *[word(400 + 150 * i, w, top=400) for i, w in enumerate("all and bake in a quick oven twenty minutes".split())]]
        lines = hand.table_text(words, dictionary=WORDS).split("\n")
        self.assertEqual(lines[:3], ["Beef broth . . . . . . 2 cups", f"Red-skinned potatoes, halved . . . . . . {cookbook.UNREAD}",
                                     "Mixed vegetables . . . . . . 3 cups"])

    def test_a_fraction_read_as_a_sign_in_a_list_line_is_unread_and_its_recipe_not_kept(self):
        def page(*amount):
            return [word(1150, "MUFFINS.", top=100),
                    *amount, word(340, "cups", top=200), word(460, "flour", top=200),
                    word(250, "2", top=260), word(290, "eggs", top=260),
                    word(250, "1", top=320), word(290, "pint", top=320), word(400, "milk", top=320),
                    word(320, "Mix", top=420), *[word(470 + 150 * i, w, top=420) for i, w in enumerate("all and bake in a hot oven until brown on the top.".split())]]
        for amount in ([word(250, "1", top=200), word(290, "%", 50, top=200)], [word(250, "1", top=200), word(290, "Y%", 50, top=200)],
                       [word(250, "1", top=200), word(290, "Y", 50, top=200)], [word(250, "Y", 50, top=200)]):
            text = hand.table_text(page(*amount), dictionary=WORDS)
            self.assertIn(f"{cookbook.UNREAD} cups flour", text, [w.text for w in amount])
            self.assertEqual([b.outcome for b in cookbook.read_by_rule(text, WORDS) if b.title], ["unreadable"], [w.text for w in amount])
        text = hand.table_text(page(word(250, "1", top=200)), dictionary=WORDS)
        self.assertEqual([b.outcome for b in cookbook.read_by_rule(text, WORDS) if b.title], ["rule"])

    def test_a_lone_figure_as_wide_as_a_fraction_sign_is_never_written(self):
        half = hand.Word(1770, 100, 38, 40, 96.0, "2")
        self.assertEqual(self.row(word(310, "Butter"), word(500, "ee", 10), half, word(1810, "Cup")), f"Butter . . . . . . {cookbook.UNREAD}",
                         "staging checkpoint 8, T18: a printed half cup was kept as '2 Cup' at the confidence bar")
        self.assertEqual(self.row(word(310, "Butter"), word(500, "ee", 10), word(1770, "2"), word(1810, "Cup")), "Butter . . . . . . 2 Cup")

    def test_an_amount_its_second_reading_does_not_confirm_is_unread(self):
        def row(again):
            return hand.table_row([word(310, "Butter"), word(500, "ee", 10), word(1770, "2"), word(1810, "Cup")], self.HEIGHT, self.EDGE, self.MIDDLE,
                                  dictionary=WORDS, reread=lambda cell: again)
        for again in ("½", "%", "1/2", "4", "", None):
            self.assertEqual(row(again), f"Butter . . . . . . {cookbook.UNREAD}", repr(again))
        self.assertEqual(row("2"), "Butter . . . . . . 2 Cup")
        self.assertEqual(row("2."), "Butter . . . . . . 2 Cup")

    def test_an_old_style_one_read_as_a_letter_or_an_unconfirmed_figure_before_a_measure_is_unread_in_a_list_line(self):
        def page(*amount, reread=None):
            words = [word(1150, "PUDDING.", top=100), word(250, "Sweet", top=200), word(400, "milk,", top=200), *amount,
                     word(250, "Seeded", top=260), word(420, "raisins", top=260),
                     word(320, "Mix", top=360), *[word(470 + 150 * i, w, top=360) for i, w in enumerate("all and bake in a slow oven one hour.".split())]]
            return hand.table_text(words, dictionary=WORDS, reread=reread)
        for one in ("I", "l"):
            self.assertIn(f"Sweet milk, {cookbook.UNREAD} cup", page(word(530, one, top=200), word(570, "cup", top=200)),
                          "staging checkpoint 8, T18: 'Sweet milk, I cup' stayed unmarked")
        self.assertIn(f"Sweet milk, {cookbook.UNREAD} cup", page(word(530, "1", top=200), word(570, "cup", top=200), reread=lambda cell: "½"))
        self.assertIn("Sweet milk, 1 cup", page(word(530, "1", top=200), word(570, "cup", top=200), reread=lambda cell: "1"))

    def test_a_two_column_index_row_keeps_each_name_with_its_own_number(self):
        words = [word(250, "Sally", top=100), word(420, "Lunn", top=100), word(560, "......", 20, top=100),
                 word(1400, "Waffles", top=100), word(1650, ".....", 15, top=100), word(2200, "26", top=100),
                 word(250, "Fruit", top=160), word(400, "Pies", top=160), word(520, "....", 20, top=160), word(1150, "89", top=160),
                 word(1400, "Crab", top=160), word(1550, "Apple", top=160), word(1720, "Jelly", top=160), word(2200, "104", top=160)]
        lines = [line for line in hand.table_text(words, dictionary=WORDS).split("\n") if line.strip()]
        self.assertFalse(any("Sally" in line and "26" in line for line in lines), lines)
        self.assertFalse(any("Fruit" in line and "104" in line for line in lines), lines)
        self.assertTrue(any("Waffles" in line and "26" in line for line in lines), lines)
        self.assertTrue(any("Crab Apple Jelly" in line and "104" in line for line in lines), lines)

    def test_a_cell_is_read_again_cropped_from_the_image_and_enlarged(self):
        try:
            from PIL import Image
        except ImportError:
            self.skipTest("Pillow is not installed (it is in the nom-hand-import image)")
        sizes = []

        def runner(command):
            with Image.open(command[1]) as cell:
                sizes.append(cell.size)
            self.assertEqual(command[2:], ["stdout", "-l", "eng", "--psm", "7"])
            return "2\n"
        page = os.path.join(FIXTURES, "table-page-whole.jpg")
        read = hand.cell_reader(page, runner)
        self.assertEqual(read([hand.Word(1000, 1000, 24, 38, 96.0, "2")]), "2")
        self.assertEqual(sizes, [((24 + 38) * hand.CELL_SCALE, (38 + 38) * hand.CELL_SCALE)])
        self.assertIsNone(hand.cell_reader(os.path.join(FIXTURES, "missing.jpg"), runner)([hand.Word(10, 10, 20, 38, 96.0, "2")]),
                          "an image that cannot be opened confirms nothing")

    def test_a_recipe_with_the_unread_mark_is_never_kept_or_sent_to_the_model(self):
        text = f"CORN BREAD.\n\nCorn meal . . . . . . 1 pint\nFlour . . . . . . {cookbook.UNREAD}\n\nMix the meal and flour, add the milk; bake in a quick oven twenty minutes.\n"
        found = cookbook.read_by_rule(text, WORDS)
        self.assertEqual([(b.outcome, b.reason) for b in found if b.title],
                         [("unreadable", "unreadable text: the page image was read, but not every amount with confidence")])


def recorded(page):
    with open(os.path.join(FIXTURES, page["file"].replace(".jpg", ".tsv")), encoding="utf-8") as handle:
        return hand.tsv_words(handle.read())


class TableReadingTests(unittest.TestCase):
    """The four page images as tesseract read them (the recorded TSV): every amount the reading keeps is the one the
    page prints, no recipe with an amount not read with confidence is kept, and the page whose every row reads is
    kept by rule exactly."""

    def check(self, text, page):
        expected = {r["title"].rstrip("."): r for r in page["recipes"]}
        printed = {line for r in page["recipes"] for line in r["expected"]}
        for line in text.split("\n"):
            row = cookbook.leader_row(line)
            if row:
                self.assertIn(row, printed, f"{page['file']}: a table row read as an amount the page does not print")
        kept = {b.record["name"]: b for b in cookbook.read_by_rule(text, WORDS) if b.outcome == "rule"}
        for name, recipe in expected.items():
            complete = all(f" . . . . . . {cookbook.UNREAD}" not in line for line in text.split("\n")
                           if line.split(" . . . ")[0] in {r[0] for r in recipe["rows"]}) and \
                len([line for line in text.split("\n") if cookbook.leader_row(line) in recipe["expected"]]) == len(recipe["rows"])
            if name in kept:
                self.assertTrue(complete, f"{page['file']}: {name} kept with a row not read")
                self.assertEqual([i["rawLine"] if isinstance(i, dict) else i for i in kept[name].record["ingredients"]], recipe["expected"])
        return kept

    def test_the_recorded_readings_keep_only_amounts_the_page_prints_and_the_whole_table_by_rule(self):
        kept = {}
        rows_read = rows = 0
        for page in TABLE_PAGES:
            text = hand.table_text(recorded(page), dictionary=WORDS)
            kept.update(self.check(text, page))
            rows += sum(len(r["rows"]) for r in page["recipes"])
            rows_read += sum(1 for line in text.split("\n") if cookbook.leader_row(line))
        self.assertEqual(sorted(kept), ["SEASONING"], "every other recipe holds a fraction or a row tesseract did not read with confidence")
        self.assertEqual((rows_read, rows), (14, 29))

    def test_the_library_text_of_a_garbled_page_gives_way_to_the_image_reading(self):
        page = TABLE_PAGES[-1]
        image_text = hand.table_text(recorded(page), dictionary=WORDS)
        with tempfile.TemporaryDirectory() as folder:
            path = os.path.join(folder, "text.json")
            garbled = "SEASONING.\n\n" + "\n".join(["ELI aa a as I pint", "Shy SS 0 Ut ee ae ay cee ise cecil (Oude B:",
                                                    "ALIN Gg | 0) Ea aaa ian a ne I pint", "BI ttace ee Sore Semel arg ys 1 ae 3 qts."]) + \
                "\n\nMix together all ingredients. Store in an airtight container.\n"
            with open(path, "w", encoding="utf-8") as handle:
                json.dump({"21": {"fulltext": garbled}}, handle)
            pages, unread = hand.loc_pages(path, WORDS, {21: "p.jpg"}, lambda p: image_text)
        self.assertEqual((pages[0]["method"], pages[0]["needsImage"], unread), ("loc-image-ocr", False, []))
        doc = {"doc": "loc-b", "source": "loc", "url": BOOK.url, "resourceUrl": "https://www.loc.gov/resource/gdc.b/", "title": "B",
               "sha256": "ab", "layout": "book", "pages": pages}
        result = hand.extract_document(doc, SOURCES["loc"], words=WORDS)
        self.assertEqual([[i["rawLine"] for i in r["recipe"]["ingredients"]] for r in result.kept], [page["recipes"][0]["expected"]])

    def test_an_image_reading_that_names_every_row_replaces_the_garbled_library_text_though_it_marks_amounts(self):
        image_text = (f"CORN BREAD.\n\nCorn meal . . . . . . {cookbook.UNREAD}\nFlour . . . . . . 1 pint\nSugar . . . . . . {cookbook.UNREAD}\n"
                      f"Milk . . . . . . {cookbook.UNREAD}\n\nMix the meal and flour, add the milk; bake in a quick oven twenty minutes.\n")
        garbled = ("CORN BREAD.\n\n" + "\n".join(["ELI aa a as I pint", "Shy SS 0 Ut ee ae ay cee ise cecil (Oude B:",
                                                 "ALIN Gg | 0) Ea aaa ian a ne I pint", "BI ttace ee Sore Semel arg ys 1 ae 3 qts."]) +
                   "\n\nMix the meal and flour, add the milk; bake in a quick oven twenty minutes.\n")
        with tempfile.TemporaryDirectory() as folder:
            path = os.path.join(folder, "text.json")
            with open(path, "w", encoding="utf-8") as handle:
                json.dump({"25": {"fulltext": garbled}}, handle)
            pages, _ = hand.loc_pages(path, WORDS, {25: "p.jpg"}, lambda p: image_text)
        self.assertEqual(pages[0]["method"], "loc-image-ocr", "staging checkpoint 8, T18: p. 25 kept the Library's garbled table")
        self.assertEqual((pages[0]["imageReading"]["unreadable"], pages[0]["imageReading"]["marked"]), (3, 3))
        self.assertEqual([b.outcome for b in cookbook.read_by_rule(pages[0]["text"], WORDS) if b.title], ["unreadable"],
                         "a marked recipe is still never kept or sent to the model")

    def test_tesseract_is_asked_for_its_words_and_boxes(self):
        commands = []
        real = hand.have
        self.addCleanup(setattr, hand, "have", real)
        hand.have = lambda tool: True
        with open(os.path.join(FIXTURES, "table-page-whole.tsv"), encoding="utf-8") as handle:
            tsv = handle.read()
        text = hand.tesseract_image("/work/loc/docs/b.p0021.jpg", runner=lambda c: commands.append(c) or tsv, words=WORDS,
                                    reread=lambda cell: cell[0].text)
        self.assertEqual(commands, [["tesseract", "/work/loc/docs/b.p0021.jpg", "stdout", "-l", "eng", "--psm", "11", "tsv"]])
        self.assertIn("Chili powder . . . . . . 2 teaspoons", text)
        hand.have = lambda tool: False
        self.assertIsNone(hand.tesseract_image("/x.jpg", runner=lambda c: "never"))

    @unittest.skipUnless(shutil.which("tesseract"), "tesseract is not installed (run in the nom-hand-import image)")
    def test_tesseract_reads_the_page_images_and_keeps_only_amounts_the_page_prints(self):
        kept = {}
        for page in TABLE_PAGES:
            text = hand.tesseract_image(os.path.join(FIXTURES, page["file"]), words=WORDS)
            kept.update(self.check(text, page))
        self.assertIn("SEASONING", kept, "the page whose every row reads is kept by rule")


if __name__ == "__main__":
    unittest.main()

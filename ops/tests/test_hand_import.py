"""
Tests for the hand import of the four approved PDF and scanned sources: the listed documents and
their hosts, the polite resumable fetch and its verification, page text from a text PDF page and a
scanned page made here, the rule readers of a recipe card and a recipe page, the model extraction of a
book with its number check, the source links by page, and the staged-import batches.
No network: the sites and the model are recorded stand-ins. pdftotext and pdftoppm (poppler) are
needed for the PDF tests, tesseract for the one real OCR test; each is skipped without its tool.
Run: python3 -m unittest discover -s ops/tests
"""

import contextlib
import importlib.util
import io
import json
import os
import re
import shlex
import shutil
import subprocess
import sys
import tempfile
import threading
import unittest
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

OPS = os.path.join(os.path.dirname(__file__), "..")
_spec = importlib.util.spec_from_file_location("hand_import", os.path.join(OPS, "hand-import.py"))
hand = importlib.util.module_from_spec(_spec)
sys.modules["hand_import"] = hand
_spec.loader.exec_module(hand)

FIXTURES = os.path.join(os.path.dirname(__file__), "fixtures", "hand-import")
POPPLER = shutil.which("pdftotext") and shutil.which("pdftoppm")
SOURCES = hand.load_sources()


def pdf_text(value: str) -> str:
    return value.replace("\\", "\\\\").replace("(", "\\(").replace(")", "\\)")


def write_pdf(path: str, pages: list) -> None:
    """A small PDF: each page is a list of (x, y, size, text) runs in Helvetica, or ("image", width, height, gray bytes)."""
    objects: list[bytes] = []

    def add(body: bytes) -> int:
        objects.append(body)
        return len(objects)
    catalog = add(b"")
    tree = add(b"")
    font = add(b"<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>")
    kids = []
    for page in pages:
        if page and page[0] == "image":
            _, width, height, gray = page
            image = add(b"<< /Type /XObject /Subtype /Image /Width %d /Height %d /ColorSpace /DeviceGray /BitsPerComponent 8 /Length %d >>\nstream\n"
                        % (width, height, len(gray)) + gray + b"\nendstream")
            content = b"q 612 0 0 792 0 0 cm /Im1 Do Q"
            resources = b"<< /XObject << /Im1 %d 0 R >> >>" % image
        else:
            content = b"".join(b"BT /F1 %d Tf %d %d Td (%s) Tj ET\n" % (size, x, y, pdf_text(text).encode("cp1252")) for x, y, size, text in page)
            resources = b"<< /Font << /F1 %d 0 R >> >>" % font
        stream = add(b"<< /Length %d >>\nstream\n" % len(content) + content + b"\nendstream")
        kids.append(add(b"<< /Type /Page /Parent %d 0 R /MediaBox [0 0 612 792] /Resources %s /Contents %d 0 R >>" % (tree, resources, stream)))
    objects[catalog - 1] = b"<< /Type /Catalog /Pages %d 0 R >>" % tree
    objects[tree - 1] = b"<< /Type /Pages /Kids [%s] /Count %d >>" % (b" ".join(b"%d 0 R" % k for k in kids), len(kids))
    out = bytearray(b"%PDF-1.4\n")
    offsets = []
    for number, body in enumerate(objects, start=1):
        offsets.append(len(out))
        out += b"%d 0 obj\n" % number + body + b"\nendobj\n"
    xref = len(out)
    out += b"xref\n0 %d\n0000000000 65535 f \n" % (len(objects) + 1)
    out += b"".join(b"%010d 00000 n \n" % offset for offset in offsets)
    out += b"trailer\n<< /Size %d /Root %d 0 R >>\nstartxref\n%d\n%%%%EOF\n" % (len(objects) + 1, catalog, xref)
    with open(path, "wb") as handle:
        handle.write(bytes(out))


CARD = [
    (220, 740, 20, "Garden Bean Skillet"),
    (150, 712, 11, "Prep: 10 minutes | Cook: 20 minutes | Total: 30 minutes"),
    (190, 694, 11, "Yield: 4 servings | Serving Size: 1 cup"),
    (40, 650, 12, "Ingredients"),
    (320, 650, 12, "Directions"),
    (40, 626, 10, "1 tablespoon olive oil"),
    (320, 626, 10, "1. Heat the oil in a large skillet over"),
    (40, 606, 10, "1/2 medium onion, diced (about"),
    (320, 612, 10, "medium heat."),
    (40, 592, 10, "1/2 cup)"),
    (320, 592, 10, "2. Add the onion and cook until soft,"),
    (40, 572, 10, "2 cups cooked green beans"),
    (320, 578, 10, "about 5 minutes."),
    (40, 552, 10, "1/4 teaspoon black pepper"),
    (320, 558, 10, "3. Stir in the beans and pepper and cook"),
    (320, 544, 10, "for 15 minutes."),
    (40, 500, 12, "Recipe Notes"),
    (40, 480, 10, "Serve with brown rice if desired; any green bean works in this dish."),
    (40, 440, 10, "Nutrition Facts Per Serving: Calories: 90 | Total Fat: 3.5 g | Sodium: 10 mg"),
]


def gray_of(pdf: str, folder: str) -> tuple[int, int, bytes]:
    """The first page of a PDF rendered to an 8-bit gray image by pdftoppm (a scan made for the test)."""
    prefix = os.path.join(folder, "scan")
    subprocess.run(["pdftoppm", "-r", "150", "-gray", "-f", "1", "-l", "1", "-singlefile", pdf, prefix], check=True)
    with open(prefix + ".pgm", "rb") as handle:
        data = handle.read()
    fields, at = [], 0
    while len(fields) < 4:
        while data[at:at + 1].isspace():
            at += 1
        if data[at:at + 1] == b"#":
            at = data.index(b"\n", at) + 1
            continue
        end = at
        while not data[end:end + 1].isspace():
            end += 1
        fields.append(data[at:end])
        at = end
    width, height = int(fields[1]), int(fields[2])
    return width, height, data[at + 1:at + 1 + width * height]


class Answer:
    """A stand-in HTTP answer."""

    def __init__(self, status, body=b"", headers=None, location=None):
        self.status, self.body, self.headers, self.location = status, body, headers or {}, location


class Site:
    """A stand-in for the hosts: answers by URL (a list is served in turn), records every request."""

    def __init__(self, routes):
        self.routes = routes
        self.calls = []

    def __call__(self, url, headers):
        self.calls.append((url, dict(headers)))
        answer = self.routes.get(url, Answer(404))
        if isinstance(answer, list):
            answer = answer.pop(0)
        body = answer.body
        return hand.Response(answer.status, {k.lower(): v for k, v in answer.headers.items()}, iter([body]) if body else iter(()), answer.location)


class Clock:
    def __init__(self):
        self.now = 0.0
        self.slept = []

    def time(self):
        return self.now

    def sleep(self, seconds):
        self.slept.append(seconds)
        self.now += seconds


PDF_BYTES = b"%PDF-1.4\n1 0 obj\n<<>>\nendobj\ntrailer\n<<>>\n%%EOF\n"
VA = "https://www.nutrition.va.gov"


def fetcher(site, clock, **delays):
    allowed = {"www.nutrition.va.gov": 5.0, "www.loc.gov": 6.0, "tile.loc.gov": 6.0, "www.cancer.gov": 5.0}
    allowed.update(delays)
    groups = {"www.nutrition.va.gov": "va", "www.loc.gov": "loc", "tile.loc.gov": "loc", "www.cancer.gov": "nci"}
    return hand.Fetcher(allowed, opener=site, clock=clock.time, sleep=clock.sleep, groups=groups)


class ListedDocumentsTests(unittest.TestCase):
    def test_the_repository_lists_only_documents_on_their_sources_hosts_with_an_attribution(self):
        books = hand.load_books()
        self.assertEqual(set(SOURCES), {"va", "nhlbi", "loc", "nci"})
        self.assertEqual({b.source for b in books}, set(SOURCES))
        for book in books:
            self.assertIn(hand.host_of(book.url), SOURCES[book.source].hosts, book.doc_id)
            if book.kind != "index":
                self.assertTrue(book.attribution, book.doc_id)
        self.assertEqual([b.url for b in books if b.source == "nci"],
                         ["https://www.cancer.gov/about-cancer/causes-prevention/risk/diet/down-home-healthy-cooking.pdf"])
        self.assertTrue(all(b.attribution.startswith("Library of Congress.") for b in books if b.source == "loc"))
        self.assertTrue(any("National Cancer Institute" in b.attribution for b in books if b.source == "nci"))

    def test_the_hand_sources_carry_clear_licence_facts_for_their_own_domain(self):
        bodies = hand.hand_bodies(SOURCES)
        self.assertEqual(len(bodies), 4)
        for body, source in zip(bodies, SOURCES.values()):
            self.assertEqual(body["licenceKind"], "public-domain")
            self.assertTrue(body["licenceStatement"] and body["licenceFoundAt"].startswith("https://"))
            self.assertIn(body["domain"], source.hosts)

    def test_a_row_off_its_sources_hosts_or_of_an_unknown_source_is_refused(self):
        with tempfile.TemporaryDirectory() as folder:
            path = os.path.join(folder, "books.csv")
            for row in ("va-x,va,pdf,https://www.example.com/x.pdf,card,X,VA",
                        "zz-x,zz,pdf,https://www.nutrition.va.gov/x.pdf,card,X,VA",
                        "va-x,va,pdf,http://www.nutrition.va.gov/x.pdf,card,X,VA",
                        "va-x,va,pdf,https://www.nutrition.va.gov/x.pdf,card,X,"):
                with open(path, "w", encoding="utf-8") as handle:
                    handle.write("doc_id,source,kind,url,layout,title,attribution\n" + row + "\n")
                with self.assertRaises(ValueError, msg=row):
                    hand.load_books(path, SOURCES)


class FetchTests(unittest.TestCase):
    def test_another_host_and_a_path_robots_disallows_are_refused_and_nothing_is_fetched_for_them(self):
        site = Site({f"{VA}/robots.txt": Answer(200, b"User-agent: *\nDisallow: /private/\n")})
        fetch = fetcher(site, Clock())
        with self.assertRaises(hand.Refused):
            fetch.read("https://www.example.com/recipe.pdf")
        with self.assertRaises(hand.Refused):
            fetch.read(f"{VA}/private/recipe.pdf")
        self.assertEqual([url for url, _ in site.calls], [f"{VA}/robots.txt"])

    def test_requests_to_a_host_are_spaced_by_the_longer_of_its_delay_and_robots_crawl_delay(self):
        site = Site({"https://www.loc.gov/robots.txt": Answer(200, b"User-agent: *\nCrawl-Delay: 9\n"),
                     "https://www.loc.gov/a": Answer(200, b"a"), "https://www.loc.gov/b": Answer(200, b"b")})
        clock = Clock()
        fetch = fetcher(site, clock)
        fetch.read("https://www.loc.gov/a")
        fetch.read("https://www.loc.gov/b")
        self.assertEqual(clock.slept, [9.0, 9.0])

    def test_a_redirect_to_another_host_is_refused(self):
        site = Site({f"{VA}/robots.txt": Answer(404), f"{VA}/a.pdf": Answer(302, location="https://cdn.example.net/a.pdf")})
        with self.assertRaises(hand.Refused):
            fetcher(site, Clock()).read(f"{VA}/a.pdf")

    def test_a_redirect_to_another_sources_host_is_refused_for_a_listed_document(self):
        url = f"{VA}/docs/c.pdf"
        site = Site({f"{VA}/robots.txt": Answer(404), url: Answer(302, location="https://www.cancer.gov/some.pdf"),
                     "https://www.cancer.gov/robots.txt": Answer(404), "https://www.cancer.gov/some.pdf": Answer(200, PDF_BYTES)})
        with tempfile.TemporaryDirectory() as out:
            store = hand.Store(out)
            report = hand.fetch([hand.Book("va-c", "va", "pdf", url, "card", "C", "VA")], SOURCES, store, fetcher(site, Clock()))
            self.assertEqual(report["counts"], {"refused": 1})
            self.assertIn("www.cancer.gov", report["refused"][0]["reason"])
            self.assertNotIn("https://www.cancer.gov/some.pdf", [u for u, _ in site.calls])
            self.assertFalse(os.path.exists(store.path("va-c.pdf")))

    def test_the_hosts_of_one_source_share_its_pacing(self):
        site = Site({"https://www.loc.gov/robots.txt": Answer(404), "https://tile.loc.gov/robots.txt": Answer(404),
                     "https://www.loc.gov/a": Answer(200, b"a"), "https://tile.loc.gov/b": Answer(200, b"b"),
                     "https://www.loc.gov/c": Answer(200, b"c")})
        clock = Clock()
        fetch = fetcher(site, clock)
        stamps = []
        for url in ("https://www.loc.gov/a", "https://tile.loc.gov/b", "https://www.loc.gov/c"):
            fetch.read(url)
            stamps.append(clock.now)
        self.assertGreaterEqual(stamps[1] - stamps[0], 6.0)
        self.assertGreaterEqual(stamps[2] - stamps[1], 6.0)

    def test_robots_that_cannot_be_read_stop_the_host_for_the_run(self):
        site = Site({f"{VA}/robots.txt": Answer(503)})
        with self.assertRaises(hand.Refused):
            fetcher(site, Clock()).read(f"{VA}/a.pdf")

    def test_a_cut_off_download_is_kept_and_resumed_by_range_then_verified_and_listed(self):
        url = f"{VA}/docs/a.pdf"
        site = Site({f"{VA}/robots.txt": Answer(404), url: [
            Answer(200, PDF_BYTES[:20], {"Content-Length": str(len(PDF_BYTES)), "Content-Type": "application/pdf"}),
            Answer(206, PDF_BYTES[20:], {"Content-Range": f"bytes 20-{len(PDF_BYTES) - 1}/{len(PDF_BYTES)}"}),
        ]})
        with tempfile.TemporaryDirectory() as out:
            store = hand.Store(out)
            book = hand.Book("va-a", "va", "pdf", url, "card", "A", "VA")
            fetch = fetcher(site, Clock())
            first = hand.fetch([book], SOURCES, store, fetch)
            self.assertEqual(first["counts"], {"refused": 1})
            self.assertEqual(os.path.getsize(store.path("va-a.pdf") + ".part"), 20)
            second = hand.fetch([book], SOURCES, store, fetch)
            self.assertEqual(second["counts"], {"fetched": 1})
            self.assertEqual(site.calls[-1][1].get("Range"), "bytes=20-")
            with open(store.path("va-a.pdf"), "rb") as handle:
                self.assertEqual(handle.read(), PDF_BYTES)
            with open(os.path.join(out, "SHA256SUMS"), encoding="utf-8") as handle:
                self.assertEqual(handle.read(), f"{hand.sha256_file(store.path('va-a.pdf'))}  docs/va-a.pdf\n")
            calls = len(site.calls)
            again = hand.fetch([book], SOURCES, hand.Store(out), fetch)
            self.assertEqual(again["counts"], {"verified": 1})
            self.assertEqual(len(site.calls), calls)

    def test_a_pdf_without_its_end_marker_or_a_page_served_for_a_pdf_is_refused(self):
        url = f"{VA}/docs/b.pdf"
        site = Site({f"{VA}/robots.txt": Answer(404), url: [Answer(200, PDF_BYTES[:-7]),
                                                             Answer(200, b"<!DOCTYPE html><html><body>moved</body></html>", {"Content-Type": "text/html"})]})
        with tempfile.TemporaryDirectory() as out:
            store = hand.Store(out)
            book = hand.Book("va-b", "va", "pdf", url, "card", "B", "VA")
            fetch = fetcher(site, Clock())
            self.assertEqual(hand.fetch([book], SOURCES, store, fetch)["counts"], {"refused": 1})
            os.remove(store.path("va-b.pdf") + ".part")
            self.assertEqual(hand.fetch([book], SOURCES, store, fetch)["counts"], {"refused": 1})
            self.assertFalse(os.path.exists(os.path.join(out, "SHA256SUMS")))

    def test_downloads_never_go_inside_the_repository(self):
        with self.assertRaises(ValueError):
            hand.Store(os.path.join(OPS, "downloads"))

    def test_a_library_item_is_taken_only_in_the_public_domain_of_1930_or_earlier_and_its_ocr_comes_from_its_record(self):
        item = {"item": {"rights_advisory": ["<p>The books in this collection are in the public domain and are free to use and reuse.</p>"],
                         "date": "1876", "language": ["english"]},
                "resources": [{"url": "https://www.loc.gov/resource/gdc.book1/",
                               "fulltext_derivative": "https://tile.loc.gov/storage-services/public/gdc/book1/book1.text.json"}]}
        self.assertIsNone(hand.loc_item_problem(item))
        later = json.loads(json.dumps(item))
        later["item"]["date"] = "1931"
        self.assertIn("1930", hand.loc_item_problem(later))
        rights = json.loads(json.dumps(item))
        rights["item"]["rights_advisory"] = ["Rights status not evaluated."]
        self.assertIn("public domain", hand.loc_item_problem(rights))
        text = json.dumps({"1": {"fulltext": ""}, "2": {"fulltext": "GINGERBREAD.\n\nOne cup of molasses."}}).encode()
        site = Site({"https://www.loc.gov/robots.txt": Answer(200, b"User-agent: *\nCrawl-Delay: 5\n"),
                     "https://www.loc.gov/item/book1/?fo=json": Answer(200, json.dumps(item).encode(), {"Content-Type": "application/json"}),
                     "https://tile.loc.gov/robots.txt": Answer(404),
                     item["resources"][0]["fulltext_derivative"]: Answer(200, text, {"Content-Type": "application/json"})})
        with tempfile.TemporaryDirectory() as out:
            store = hand.Store(out)
            book = hand.Book("loc-book1", "loc", "loc-item", "https://www.loc.gov/item/book1/", "book", "Book (1876)", "Library of Congress. Book (1876)")
            self.assertEqual(hand.fetch([book], SOURCES, store, fetcher(site, Clock()))["counts"], {"fetched": 1})
            doc = hand.text_document(book, store)
            self.assertEqual(doc["resourceUrl"], "https://www.loc.gov/resource/gdc.book1/")
            self.assertEqual([(p["n"], p["method"]) for p in doc["pages"]], [(1, "loc-blank"), (2, "loc-ocr")],
                             "a book's blank page needs no tesseract; checkpoints 3 to 6 skipped the books for it")

    def test_index_pages_offer_unlisted_documents_on_their_hosts_only(self):
        page = ('<a href="/NUTRITION/docs/Recipes/A.pdf#">A dish</a><a href="https://www.example.com/x.pdf">x</a>'
                '<a href="/NUTRITION/Recipes/Other.asp">Other</a><a href="/NUTRITION/docs/Recipes/A.pdf">again</a>')
        found = hand.document_links(page, f"{VA}/NUTRITION/Recipes/Breakfast/Eggs.asp", ["www.nutrition.va.gov"])
        self.assertEqual(found, [(f"{VA}/NUTRITION/docs/Recipes/A.pdf", "A dish")])
        recipes = '<a href="/r/salmon">Salmon</a><a href="?page=2">next</a>'
        self.assertEqual(hand.document_links(recipes, "https://www.nhlbi.nih.gov/r", ["www.nhlbi.nih.gov"], pages=True),
                         [("https://www.nhlbi.nih.gov/r/salmon", "Salmon")])


@unittest.skipUnless(POPPLER, "pdftotext and pdftoppm (poppler-utils) are not installed")
class PdfTextTests(unittest.TestCase):
    def setUp(self):
        self.folder = tempfile.mkdtemp()
        self.addCleanup(shutil.rmtree, self.folder)
        self.card = os.path.join(self.folder, "card.pdf")
        write_pdf(self.card, [CARD, [(72, 700, 12, "Second page of the card, no recipe here at all.")]])

    def scanned(self) -> str:
        width, height, gray = gray_of(self.card, self.folder)
        path = os.path.join(self.folder, "scan.pdf")
        write_pdf(path, [[(72, 700, 12, "A cover page with words on it for the text layer.")], ("image", width, height, gray)])
        return path

    def test_a_text_pdf_keeps_its_pages_and_reads_the_left_column_before_the_right(self):
        pages = hand.pdf_pages(self.card, "card")
        self.assertEqual([(p["n"], p["method"]) for p in pages], [(1, "pdftotext"), (2, "pdftotext")])
        text = pages[0]["text"]
        self.assertLess(text.index("1/4 teaspoon black pepper"), text.index("Directions"))
        self.assertLess(text.index("Directions"), text.index("for 15 minutes."))
        record, why = hand.parse_card("Garden Bean Skillet", text)
        self.assertIsNone(why)
        self.assertEqual([i["rawLine"] for i in record["ingredients"]],
                         ["1 tablespoon olive oil", "1/2 medium onion, diced (about 1/2 cup)", "2 cups cooked green beans", "1/4 teaspoon black pepper"])
        self.assertEqual([s["instruction"] for s in record["steps"]],
                         ["Heat the oil in a large skillet over medium heat.", "Add the onion and cook until soft, about 5 minutes.",
                          "Stir in the beans and pepper and cook for 15 minutes."])
        self.assertEqual((record["recipeServings"], record["prepTimeMinutes"], record["totalTimeMinutes"]), (4.0, 10, 30))
        self.assertIsNone(hand.number_check(record, text))

    def test_a_scanned_page_goes_to_ocr_with_its_page_number_and_is_marked_when_ocr_is_missing(self):
        scan = self.scanned()
        asked = []
        pages = hand.pdf_pages(scan, "card", ocr=lambda path, n: asked.append(n) or "Read by OCR")
        self.assertEqual(asked, [2])
        self.assertEqual([(p["n"], p["method"], p["text"]) for p in pages][1], (2, "tesseract", "Read by OCR"))
        unread = hand.pdf_pages(scan, "card", ocr=lambda path, n: None)
        self.assertEqual([(p["n"], p["method"]) for p in unread], [(1, "pdftotext"), (2, "needs-ocr")])

    @unittest.skipUnless(shutil.which("tesseract"), "tesseract is not installed")
    def test_tesseract_reads_the_scanned_page(self):
        pages = hand.pdf_pages(self.scanned(), "card")
        self.assertEqual(pages[1]["method"], "tesseract")
        self.assertIn("Garden Bean Skillet", pages[1]["text"])


CARD_TEXT = """Garden Bean Skillet

Prep: 10 minutes | Cook: 20 minutes | Total: 30 minutes

Yield: 4 servings | Serving Size: 1 cup

Ingredients

1 tablespoon olive oil

1/2 medium onion, diced (about
1/2 cup)

2 cups cooked green beans

Directions

1. Heat the oil in a skillet.

2. Add the onion and beans and cook for 15 minutes.

Recipe Notes

Any green bean works.
"""


def doc(layout, pages, **extra):
    return {"doc": "d1", "source": "va", "url": "https://www.cancer.gov/book.pdf", "title": extra.pop("title", None), "sha256": "ab",
            "layout": layout, "pages": [{"n": n, "method": "pdftotext", "text": t} for n, t in pages], **extra}


class RecordTests(unittest.TestCase):
    def test_a_card_whose_amount_is_not_in_its_page_is_dropped_and_counted_never_repaired(self):
        kept = hand.extract_document(doc("card", [(1, CARD_TEXT)], title="Garden Bean Skillet"), SOURCES["va"])
        self.assertEqual(len(kept.kept), 1)
        self.assertEqual(kept.kept[0]["recipe"]["sourceUrl"], "https://www.cancer.gov/book.pdf")
        self.assertNotIn("publicDomain", kept.kept[0]["recipe"])
        record, _ = hand.parse_card("Garden Bean Skillet", CARD_TEXT)
        record["ingredients"][2]["rawLine"] = "3 cups cooked green beans"
        self.assertIn("number check", hand.number_check(record, CARD_TEXT))
        record, _ = hand.parse_card("Garden Bean Skillet", CARD_TEXT)
        record["recipeYield"] = "6 servings"
        self.assertIn("recipeYield", hand.number_check(record, CARD_TEXT))
        unparsed = hand.extract_document(doc("card", [(1, "Garden Bean Skillet\n\nA page about beans.")]), SOURCES["va"])
        self.assertEqual((len(unparsed.kept), len(unparsed.unparsed)), (0, 1))

    def test_a_wrapped_ingredient_line_is_joined_across_a_block_break_and_an_amount(self):
        text = CARD_TEXT.replace("2 cups cooked green beans\n",
                                 "4 cups fresh vegetables, trimmed and cut into\n\n½- to 1-inch pieces (one type or a combination)\n\n"
                                 "8 ounces whole-wheat pasta, or\n\n1 (16-ounce) bag frozen vegetables\n\n"
                                 "Salt and pepper, to\n\ntaste\n\n2 cups cooked green beans\n")
        record, why = hand.parse_card("Garden Bean Skillet", text)
        self.assertIsNone(why)
        self.assertEqual([i["rawLine"] for i in record["ingredients"]], [
            "1 tablespoon olive oil",
            "1/2 medium onion, diced (about 1/2 cup)",
            "4 cups fresh vegetables, trimmed and cut into ½- to 1-inch pieces (one type or a combination)",
            "8 ounces whole-wheat pasta, or 1 (16-ounce) bag frozen vegetables",
            "Salt and pepper, to taste",
            "2 cups cooked green beans",
        ], "staging checkpoint 3, N29: Marinated Vegetables, Pasta Skillet and Overnight Oats split one line in two")

    def test_a_line_ending_in_if_or_a_word_that_names_no_food_alone_or_followed_by_a_bracket_only_line_is_joined(self):
        text = CARD_TEXT.replace("2 cups cooked green beans\n",
                                 "1/2 cup lowfat milk, adding more for a thinner consistency if\n\ndesired\n\n"
                                 "1 tablespoon mayonnaise or plain\n\nGreek yogurt\n\n"
                                 "1/2 cup raw cashews or peanuts\n\n(optional)\n\n"
                                 "Optional toppings: Chopped basil, grated\n\nParmesan cheese\n\n"
                                 "1/4 cup Parmesan cheese, grated\n\nCooking spray\n\n2 cups cooked green beans\n")
        record, why = hand.parse_card("Garden Bean Skillet", text)
        self.assertIsNone(why)
        self.assertEqual([i["rawLine"] for i in record["ingredients"]][2:], [
            "1/2 cup lowfat milk, adding more for a thinner consistency if desired",
            "1 tablespoon mayonnaise or plain Greek yogurt",
            "1/2 cup raw cashews or peanuts (optional)",
            "Optional toppings: Chopped basil, grated Parmesan cheese",
            "1/4 cup Parmesan cheese, grated",
            "Cooking spray",
            "2 cups cooked green beans",
        ], "staging checkpoint 4, P26: five VA recipes still split a wrapped line; a line with an amount that ends in ', grated' is whole")

    def test_a_wrapped_notes_line_is_no_heading_and_a_card_keeping_far_fewer_lines_than_its_list_fails_the_readers_check(self):
        wrapped = CARD_TEXT.replace("1 tablespoon olive oil\n",
                                    "2 cups corn flour (masa harina; see Recipe\nNotes)\n\n1 tablespoon olive oil\n")
        record, why = hand.parse_card("Garden Bean Skillet", wrapped)
        self.assertIsNone(why)
        self.assertEqual([i["rawLine"] for i in record["ingredients"]], [
            "2 cups corn flour (masa harina; see Recipe Notes)",
            "1 tablespoon olive oil",
            "1/2 medium onion, diced (about 1/2 cup)",
            "2 cups cooked green beans",
        ], "staging checkpoint 5, Q23: the wrapped 'Notes)' ended the list and the arepas card kept 1 of 12 lines")
        self.assertIsNone(hand.number_check(record, wrapped))
        self.assertEqual(len(hand.parse_card("Garden Bean Skillet", CARD_TEXT)[0]["ingredients"]), 3, "the notes heading alone still ends the list")

        cut = wrapped.replace("Notes)\n", "Notes\n")
        record, why = hand.parse_card("Garden Bean Skillet", cut.replace("1 tablespoon olive oil\n", "1 tablespoon olive oil\n\n2 cloves garlic\n\n1 lemon\n"))
        self.assertIsNone(record)
        self.assertIn("reader check", why)

    def test_a_line_after_de_is_joined_and_a_line_left_apart_inside_an_ingredient_block_fails_the_readers_check(self):
        toppings = CARD_TEXT.replace("2 cups cooked green beans\n",
                                     "2 cups cooked green beans\n\nOptional toppings: Shredded cheese, Pico de\nGallo, black beans (drained and\nrinsed)\n")
        record, why = hand.parse_card("Garden Bean Skillet", toppings)
        self.assertIsNone(why)
        self.assertEqual(record["ingredients"][-1]["rawLine"], "Optional toppings: Shredded cheese, Pico de Gallo, black beans (drained and rinsed)",
                         "review of staging checkpoint 5, Q23: the arepas card split 'Pico de' / 'Gallo, ...' into two ingredients")

        apart = CARD_TEXT.replace("1 tablespoon olive oil\n", "1 tablespoon olive oil\nNonstick cooking spray\n")
        record, why = hand.parse_card("Garden Bean Skillet", apart)
        self.assertIsNone(record)
        self.assertIn("reader check", why)
        self.assertIn("Nonstick cooking spray", why)

    def test_a_second_numbered_part_of_the_method_is_kept_with_its_heading_and_a_record_short_of_a_numbered_step_fails_the_check(self):
        parts = CARD_TEXT.replace("2. Add the onion and beans and cook for 15 minutes.\n",
                                  "2. Add the onion and beans and cook for 15 minutes.\n\nServe as a Salad\n\n"
                                  "1. Chill the beans for 1 hour and toss them with\n2 tablespoons vinegar.\n")
        record, why = hand.parse_card("Garden Bean Skillet", parts)
        self.assertIsNone(why)
        self.assertEqual(record["steps"], [
            {"order": 1, "instruction": "Heat the oil in a skillet."},
            {"order": 2, "instruction": "Add the onion and beans and cook for 15 minutes."},
            {"order": 3, "instruction": "Chill the beans for 1 hour and toss them with 2 tablespoons vinegar.", "section": "Serve as a Salad"},
        ], "staging checkpoint 6, R20: va-ranch-seasoning-mix kept 2 of 3 steps, its 'Make Dip or Dressing' part was dropped")
        self.assertIsNone(hand.number_check(record, parts))
        self.assertIsNone(hand.steps_check(record, parts))
        kept = hand.extract_document(doc("card", [(1, parts)], title="Garden Bean Skillet"), SOURCES["va"])
        self.assertEqual((len(kept.kept), len(kept.rejects)), (1, 0))

        record["steps"] = record["steps"][:2]
        self.assertIsNone(hand.number_check(record, parts), "the number check finds only what a record holds in its page")
        self.assertIn("keeps 2 of the 3 numbered steps", hand.steps_check(record, parts))

        noted = CARD_TEXT.replace("Recipe Notes\n\nAny green bean works.\n", "Any green bean works.\n")
        record, why = hand.parse_card("Garden Bean Skillet", noted)
        self.assertEqual([s["instruction"] for s in record["steps"]], ["Heat the oil in a skillet.", "Add the onion and beans and cook for 15 minutes."],
                         "a block after the steps that heads no part numbered from 1 is still a note")
        self.assertIsNone(hand.steps_check(record, noted))

    def test_each_named_time_of_a_card_header_is_read_and_a_chill_or_inactive_time_is_not_kept_on_its_own(self):
        for header, expected in [
            ("Prep: 15 minutes | Chill: 20 minutes| Cook: 20 minutes | Total: 55 minutes", (15, 20, 55)),
            ("Prep: 10 minutes | Inactive: 30 minutes | Total: 40 minutes", (10, None, 40)),
            ("Prep: 5 minutes | Cook: 10 minutes | Chill: 2 hours | Total: 2 hours 15 minutes", (5, 10, 135)),
        ]:
            record, why = hand.parse_card("Garden Bean Skillet", CARD_TEXT.replace("Prep: 10 minutes | Cook: 20 minutes | Total: 30 minutes", header))
            self.assertIsNone(why)
            self.assertEqual((record.get("prepTimeMinutes"), record.get("cookTimeMinutes"), record.get("totalTimeMinutes")), expected,
                             f"staging checkpoint 4, P27: a header with a Chill or Inactive field gave no time at all ({header})")
            self.assertNotIn("chillTime", record)
            self.assertIsNone(hand.number_check(record, CARD_TEXT.replace("Prep: 10 minutes | Cook: 20 minutes | Total: 30 minutes", header)))

    def test_a_cards_name_is_its_printed_title_and_the_listed_title_only_when_it_prints_none(self):
        printed = CARD_TEXT.replace("Garden Bean Skillet\n", "Whole-Wheat Garden\nBean Skillet\n")
        self.assertEqual(hand.parse_card("Whole Wheat Garden Bean Skillet", printed)[0]["name"], "Whole-Wheat Garden Bean Skillet",
                         "staging checkpoint 4, P28: 53 of 337 VA names came from the index list, not the card")
        placeholder = CARD_TEXT.replace("Garden Bean Skillet\n", "Recipe Title\nGarden Bean Sauté\n")
        self.assertEqual(hand.parse_card("Garden Bean Saute", placeholder)[0]["name"], "Garden Bean Sauté")
        headless = CARD_TEXT.replace("Prep: 10 minutes | Cook: 20 minutes | Total: 30 minutes\n", "").replace("Yield: 4 servings | Serving Size: 1 cup\n", "")
        self.assertEqual(hand.parse_card("Garden Bean Skillet (listed)", headless)[0]["name"], "Garden Bean Skillet (listed)")
        result = hand.extract_document(doc("card", [(1, printed)], title="Whole Wheat Garden Bean Skillet"), SOURCES["va"])
        self.assertEqual(result.kept[0]["recipe"]["name"], "Whole-Wheat Garden Bean Skillet")

    def test_a_page_whose_model_call_times_out_is_dropped_after_one_timeout_and_reported_and_the_run_goes_on(self):
        pages = [(20, "PEPPER RUB\n\nINGREDIENTS\n\n1 teaspoon black pepper\n\nRub on the meat."),
                 (21, "SALT RUB\n\nINGREDIENTS\n\n1 teaspoon salt\n\nRub on the fish.")]
        answers = {pages[0][1]: {"recipes": [{"name": "Pepper Rub", "ingredients": ["1 teaspoon black pepper"], "steps": ["Rub on the meat."]}]},
                   pages[1][1]: {"recipes": [{"name": "Salt Rub", "ingredients": ["1 teaspoon salt"], "steps": ["Rub on the fish."]}]}}
        asked = []

        def stuck_host(system, text):
            asked.append(text)
            if text == pages[0][1]:
                raise hand.cookbook.urllib.error.URLError(TimeoutError("timed out"))
            return json.dumps(answers[text])

        with tempfile.TemporaryDirectory() as folder:
            ask = hand.cookbook.resumable(stuck_host, os.path.join(folder, "d1.answers.jsonl"), retries=2, wait=0, sleep=lambda s: None, retry_timeouts=False)
            result = hand.extract_document(doc("pages", pages), SOURCES["nci"], ask=ask)
        self.assertEqual(asked.count(pages[0][1]), 1, "staging checkpoint 4, P34: a page that never answers held the run for three long timeouts")
        self.assertEqual([r["recipe"]["name"] for r in result.kept], ["Salt Rub"])
        self.assertEqual((result.rejects[0]["page"], result.rejects[0]["reason"].split(":")[0]), (20, "model call timed out"))
        self.assertEqual(hand.summarize({"nci": [("d1", result)]})["nci"]["droppedByReason"], {"model call timed out": 1})

    def test_a_letter_spaced_title_is_closed_up_into_its_words(self):
        page = "C AT F I S H STEW with RICE\n\nINGREDIENTS\n\n1 pound catfish fillets\n\n1 cup rice\n\nSimmer the catfish for 10 minutes."
        record, why = hand.paged_record({"name": "C AT F I S H STEW with RICE", "ingredients": ["1 pound catfish fillets", "1 cup rice"],
                                         "steps": ["Simmer the catfish for 10 minutes."]}, page)
        self.assertIsNone(why)
        self.assertEqual(record["name"], "CATFISH STEW with RICE", "staging checkpoint 4, P34: names kept the PDF's letter spacing")
        self.assertEqual(hand.despaced("2 – M I N U T E C H I C K E N C R E O L E"), "2 – MINUTE CHICKEN CREOLE")
        self.assertEqual(hand.despaced("N E W O R L E A N S R E D B E A N S"), "NEW ORLEANS RED BEANS")
        self.assertEqual(hand.despaced("Q X Z Stew"), "QXZ Stew")
        self.assertEqual(hand.despaced("A Pot of Greens"), "A Pot of Greens")

    def test_the_recipe_video_widget_after_the_directions_is_no_step(self):
        with open(os.path.join(FIXTURES, "recipe-page.html"), encoding="utf-8") as handle:
            page = handle.read().replace("</ol>\n", "</ol>\n<div class=\"video\"><h3>Recipe Video</h3></div>\n")
        title, text = hand.html_page(page)
        record, why = hand.parse_html_recipe(title, text)
        self.assertIsNone(why)
        self.assertEqual([s["instruction"] for s in record["steps"]], ["Toss the barley with the lemon juice and salt.", "Chill for 1 hour before serving."],
                         "staging checkpoint 3, N30: 7 of 54 NHLBI recipes kept 'Recipe Video' as their last step")

    def test_a_model_call_that_times_out_drops_only_its_page_and_a_second_run_resumes_from_the_answers_kept(self):
        pages = [(20, "PEPPER RUB\n\nINGREDIENTS\n\n1 teaspoon black pepper\n\nRub on the meat."),
                 (21, "SALT RUB\n\nINGREDIENTS\n\n1 teaspoon salt\n\nRub on the fish.")]
        answers = {pages[0][1]: {"recipes": [{"name": "Pepper Rub", "ingredients": ["1 teaspoon black pepper"], "steps": ["Rub on the meat."]}]},
                   pages[1][1]: {"recipes": [{"name": "Salt Rub", "ingredients": ["1 teaspoon salt"], "steps": ["Rub on the fish."]}]}}
        asked = []

        def busy_host(system, text):
            asked.append(text)
            if text == pages[1][1] and len(asked) < 4:
                raise TimeoutError("timed out")
            return json.dumps(answers[text])

        with tempfile.TemporaryDirectory() as folder:
            checkpoint = os.path.join(folder, "d1.answers.jsonl")
            first = hand.extract_document(doc("pages", pages), SOURCES["nci"], ask=hand.cookbook.resumable(busy_host, checkpoint, retries=1, wait=0, sleep=lambda s: None))
            self.assertEqual([r["recipe"]["name"] for r in first.kept], ["Pepper Rub"], "staging checkpoint 3, N31: a timeout ended the run and lost every record")
            self.assertTrue(first.rejects[0]["reason"].startswith("model call failed"))
            second = hand.extract_document(doc("pages", pages), SOURCES["nci"], ask=hand.cookbook.resumable(busy_host, checkpoint, retries=1, wait=0, sleep=lambda s: None))
            self.assertEqual([r["recipe"]["name"] for r in second.kept], ["Pepper Rub", "Salt Rub"])
            self.assertEqual(asked.count(pages[0][1]), 1, "an answered page is read back from the checkpoint, never asked again")

    def test_a_recipe_page_is_read_from_its_html_without_its_nutrition_facts(self):
        with open(os.path.join(FIXTURES, "recipe-page.html"), encoding="utf-8") as handle:
            title, text = hand.html_page(handle.read())
        self.assertEqual(title, "Lemon Barley Salad")
        result = hand.extract_document({**doc("html", [(1, text)], title=title), "url": "https://www.nhlbi.nih.gov/r/lemon-barley-salad"}, SOURCES["nhlbi"])
        self.assertEqual(len(result.kept), 1)
        recipe = result.kept[0]["recipe"]
        self.assertEqual(recipe["name"], "Lemon Barley Salad")
        self.assertEqual(recipe["description"], "A bright grain salad for warm days.")
        self.assertEqual([i["rawLine"] for i in recipe["ingredients"]], ["1 C cooked barley", "2 Tbsp lemon juice", "1/4 tsp salt"])
        self.assertEqual(len(recipe["steps"]), 2)
        self.assertEqual((recipe["recipeServings"], recipe["prepTimeMinutes"]), (4.0, 10))
        self.assertNotIn("Calories", json.dumps(recipe))
        self.assertEqual(recipe["sourceSite"], "National Heart, Lung, and Blood Institute")

    def test_a_book_page_by_page_keeps_each_recipes_page_and_drops_a_record_whose_number_the_page_does_not_state(self):
        pages = [(20, "HOT'N SPICY SEASONING\n\nINGREDIENTS\n\n1/4 cup paprika\n\n2 tablespoons\ndried oregano, crushed\n\n"
                      "Mix together all ingredients. Store in airtight container."),
                 (21, "PEPPER RUB\n\nINGREDIENTS\n\n1 teaspoon black pepper\n\n1\n\nRub on the meat.")]
        answers = {
            pages[0][1]: {"recipes": [{"name": "Hot 'n Spicy Seasoning", "ingredients": ["1/4 cup paprika", "2 tablespoons dried oregano, crushed"],
                                       "steps": ["Mix together all ingredients.", "Store in airtight container."]}]},
            pages[1][1]: {"recipes": [{"name": "Pepper Rub", "ingredients": ["2 teaspoons black pepper"], "steps": ["Rub on the meat."]}]},
        }
        result = hand.extract_document(doc("pages", pages), SOURCES["nci"], ask=lambda system, text: json.dumps(answers[text]))
        self.assertEqual([(r["recipe"]["name"], r["pages"], r["recipe"]["sourceUrl"]) for r in result.kept],
                         [("Hot 'n Spicy Seasoning", [20], "https://www.cancer.gov/book.pdf#page=20&search=%22Hot%20%27n%20Spicy%20Seasoning%22")])
        self.assertEqual(len(result.rejects), 1)
        self.assertTrue(result.rejects[0]["reason"].startswith("number check"))
        self.assertEqual(result.rejects[0]["page"], 21)

    def test_a_page_record_that_leaves_out_a_listed_ingredient_is_dropped(self):
        page = "SUCCOTASH\n\nINGREDIENTS\n\n10-ounce baby lima\nbeans (frozen)\n\n10-ounce cut okra\n\n1\n\nCombine and simmer for 20 minutes."
        record, why = hand.paged_record({"name": "Succotash", "ingredients": ["10-ounce baby lima beans (frozen)"],
                                         "steps": ["Combine and simmer for 20 minutes."]}, page)
        self.assertIsNone(record)
        self.assertIn("10-ounce cut okra", why)
        record, why = hand.paged_record({"name": "Succotash", "ingredients": ["10-ounce baby lima beans (frozen)", "10-ounce cut okra"],
                                         "steps": ["Combine and simmer for 20 minutes."]}, page)
        self.assertIsNone(why)

    def test_a_book_across_pages_links_each_recipe_to_its_library_page_and_names_it_when_two_share_a_page(self):
        pages = [(50, "GINGERBREAD\n\n1 cup molasses\n1/2 cup butter\n\nMix and bake.\n\nCORN MUFFINS\n\n2 cups meal\n1 egg\n\nBeat and bake."),
                 (51, "SPONGE CAKE\n\n3 eggs\n1 cup sugar\n\nBeat the eggs and sugar; bake.")]

        def ask(system, text):
            recipes = []
            if "GINGERBREAD" in text:
                recipes.append({"name": "Gingerbread", "ingredients": ["1 cup molasses", "1/2 cup butter"], "steps": ["Mix and bake."]})
            if "CORN MUFFINS" in text:
                recipes.append({"name": "Corn Muffins", "ingredients": ["2 cups meal", "1 egg"], "steps": ["Beat and bake."]})
            if "SPONGE CAKE" in text:
                recipes.append({"name": "Sponge Cake", "ingredients": ["3 eggs", "1 cup sugar"], "steps": ["Beat the eggs and sugar; bake."]})
            return json.dumps({"recipes": recipes})
        book = doc("book", pages, resourceUrl="https://www.loc.gov/resource/gdc.book1/")
        result = hand.extract_document(book, SOURCES["loc"], ask=ask)
        links = {r["recipe"]["name"]: (r["pages"], r["recipe"]["sourceUrl"]) for r in result.kept}
        self.assertEqual(links["Sponge Cake"], ([51], "https://www.loc.gov/resource/gdc.book1/?sp=51&st=text&q=Sponge%20Cake"))
        self.assertEqual(links["Gingerbread"], ([50], "https://www.loc.gov/resource/gdc.book1/?sp=50&st=text&q=Gingerbread"))
        self.assertEqual(links["Corn Muffins"], ([50], "https://www.loc.gov/resource/gdc.book1/?sp=50&st=text&q=Corn%20Muffins"))
        self.assertEqual(result.rejects, [])

    def test_a_recipes_link_does_not_depend_on_which_other_records_a_run_keeps_on_its_page(self):
        pages = [(50, "GINGERBREAD\n\n1 cup molasses\n1/2 cup butter\n\nMix and bake.\n\nCORN MUFFINS\n\n2 cups meal\n1 egg\n\nBeat and bake.")]
        gingerbread = {"name": "Gingerbread", "ingredients": ["1 cup molasses", "1/2 cup butter"], "steps": ["Mix and bake."]}
        muffins = {"name": "Corn Muffins", "ingredients": ["2 cups meal", "1 egg"], "steps": ["Beat and bake."]}
        book = doc("book", pages, resourceUrl="https://www.loc.gov/resource/gdc.book1/")
        alone = hand.extract_document(book, SOURCES["loc"], ask=lambda system, text: json.dumps({"recipes": [dict(gingerbread)]}))
        both = hand.extract_document(book, SOURCES["loc"], ask=lambda system, text: json.dumps({"recipes": [dict(gingerbread), dict(muffins)]}))
        url = {r["recipe"]["name"]: r["recipe"]["sourceUrl"] for r in both.kept}["Gingerbread"]
        self.assertEqual([r["recipe"]["sourceUrl"] for r in alone.kept], [url])
        pdf = doc("pages", [(20, "HOT'N SPICY SEASONING\n\n1/4 cup paprika\n\nMix together all ingredients.")])
        seasoning = {"name": "Hot 'n Spicy Seasoning", "ingredients": ["1/4 cup paprika"], "steps": ["Mix together all ingredients."]}
        one = hand.extract_document(pdf, SOURCES["nci"], ask=lambda system, text: json.dumps({"recipes": [dict(seasoning)]}))
        self.assertIn("&search=", one.kept[0]["recipe"]["sourceUrl"])

    def test_a_book_needs_the_model_host(self):
        result = hand.extract_document(doc("book", [(1, "GINGERBREAD\n\n1 cup molasses")]), SOURCES["loc"])
        self.assertEqual(result.unparsed[0]["reason"], "a book needs the model host (--host, --model)")

    def test_running_heads_and_page_numbers_are_taken_off_the_page_edges(self):
        pages = [f"{n} HOME COOK BOOK.\n\nRECIPE {n}\n\nOne cup of milk.\n\n{n}" for n in range(20, 24)]
        stripped = hand.strip_running_heads(pages)
        self.assertTrue(all("HOME COOK BOOK" not in p for p in stripped))
        self.assertTrue(all(p.strip().endswith("One cup of milk.") for p in stripped))
        self.assertIn("RECIPE 21", stripped[1])


BOX = os.path.join(OPS, "hand-import-box.sh")
DOCKERFILE = os.path.join(OPS, "hand-import.Dockerfile")
PEPPER = "PEPPER RUB\n\nINGREDIENTS\n\n1 teaspoon black pepper\n\nRub on the meat."
SALT = "SALT RUB\n\nINGREDIENTS\n\n1 teaspoon salt\n\nRub on the fish."
SAGE = "SAGE RUB\n\nINGREDIENTS\n\n1 teaspoon sage\n\nRub on the pork."
RUBS = {PEPPER: "Pepper Rub", SALT: "Salt Rub", SAGE: "Sage Rub"}


def rub_answer(text):
    lines = [line for line in text.split("\n") if line]
    return json.dumps({"recipes": [{"name": RUBS[text], "ingredients": [lines[2]], "steps": [lines[3]]}]})


class ModelHostTests(unittest.TestCase):
    def test_a_host_is_in_use_when_a_loaded_models_expiry_moves_or_a_model_loads_between_two_readings(self):
        def readings(*answers):
            queue = list(answers)

            def read():
                answer = queue.pop(0)
                if isinstance(answer, Exception):
                    raise answer
                return answer
            return read
        idle = [{"name": "qwen2.5:14b-instruct", "expires_at": "2026-11-24T11:20:00Z"}]
        self.assertFalse(hand.host_in_use(readings(idle, idle), sleep=lambda s: None))
        self.assertFalse(hand.host_in_use(readings([], []), sleep=lambda s: None))
        moving = [{"name": "mistral-nemo", "expires_at": "2026-11-24T11:15:01Z"}]
        moved = [{"name": "mistral-nemo", "expires_at": "2026-11-24T11:15:03Z"}]
        self.assertTrue(hand.host_in_use(readings(moving, moved), sleep=lambda s: None), "staging checkpoint 6, section 28: expires_at moving with the clock")
        self.assertTrue(hand.host_in_use(readings([], moving), sleep=lambda s: None))
        self.assertTrue(hand.host_in_use(readings(hand.cookbook.urllib.error.URLError("refused")), sleep=lambda s: None))

    def test_a_warm_model_in_use_leaves_its_expiry_alone_so_the_host_is_asked_a_one_token_request(self):
        warm = [{"name": "qwen2.5vl:7b", "expires_at": "2026-11-25T10:42:00Z"}]

        def same():
            return [dict(m) for m in warm]
        asked = []

        def answers(reply):
            def answer():
                asked.append(reply)
                return reply
            return answer
        self.assertTrue(hand.host_in_use(same, sleep=lambda s: None, answers=answers(False)),
                        "staging checkpoint 6, section 28, 10:42Z: qwen2.5vl:7b loaded and in use, its expires_at unmoved")
        self.assertFalse(hand.host_in_use(same, sleep=lambda s: None, answers=answers(True)))
        moving = iter([[{"name": "mistral-nemo", "expires_at": "2026-11-24T11:15:01Z"}], [{"name": "mistral-nemo", "expires_at": "2026-11-24T11:15:03Z"}]])
        self.assertTrue(hand.host_in_use(lambda: next(moving), sleep=lambda s: None, answers=answers(True)))
        self.assertEqual(asked, [False, True], "no request of its own is sent to a host the readings already show in use")

    def test_the_one_token_request_uses_the_real_calls_context_size_and_a_host_that_holds_it_past_the_limit_is_in_use(self):
        bodies, delay = [], [0.0]
        release = threading.Event()

        class Host(BaseHTTPRequestHandler):
            def do_POST(self):
                bodies.append(json.loads(self.rfile.read(int(self.headers["Content-Length"]))))
                release.wait(delay[0])
                reply = json.dumps({"message": {"role": "assistant", "content": "1"}}).encode()
                self.send_response(200)
                self.send_header("Content-Type", "application/json")
                self.send_header("Content-Length", str(len(reply)))
                self.end_headers()
                self.wfile.write(reply)

            def log_message(self, *args):
                pass
        server = ThreadingHTTPServer(("127.0.0.1", 0), Host)
        threading.Thread(target=server.serve_forever, daemon=True).start()
        self.addCleanup(server.server_close)
        self.addCleanup(server.shutdown)
        self.addCleanup(release.set)
        url = f"http://127.0.0.1:{server.server_address[1]}"
        self.assertTrue(hand.ollama_answers(url, "qwen2.5:14b-instruct", timeout=5))
        delay[0] = 5.0
        self.assertFalse(hand.ollama_answers(url, "qwen2.5:14b-instruct", timeout=0.3), "a request queued behind another job")
        self.assertFalse(hand.ollama_answers("http://127.0.0.1:9", "qwen2.5:14b-instruct", timeout=1))
        self.assertEqual(bodies[0]["model"], "qwen2.5:14b-instruct")
        self.assertEqual(bodies[0]["options"], {**hand.cookbook.OLLAMA_OPTIONS, "num_predict": 1},
                         "the same context size as the real calls, so the model is not reloaded")

    def test_extract_reads_the_host_with_its_own_request_within_answer_within(self):
        seen = {}
        real = (hand.host_in_use, hand.ollama_answers, hand.ollama_ps)
        self.addCleanup(lambda: (setattr(hand, "host_in_use", real[0]), setattr(hand, "ollama_answers", real[1]), setattr(hand, "ollama_ps", real[2])))

        def in_use(read, probe, answers=None, **kw):
            seen["answers"] = answers is not None and answers()
            return False
        hand.host_in_use = in_use
        hand.ollama_answers = lambda host, model, timeout: seen.setdefault("asked", (host, model, timeout)) is not None
        hand.ollama_ps = lambda host: []
        gate_calls = []
        real_gate = hand.HostGate

        class Gate(real_gate):
            def __call__(self, system, user):
                gate_calls.append(self.in_use())
                raise hand.ModelHostBusy("stop")
        hand.HostGate = Gate
        self.addCleanup(setattr, hand, "HostGate", real_gate)
        with tempfile.TemporaryDirectory() as folder:
            os.makedirs(os.path.join(folder, "text"))
            books = os.path.join(folder, "books.csv")
            with open(books, "w", encoding="utf-8") as handle:
                handle.write("doc_id,source,kind,url,layout,title,attribution\n")
                handle.write("d1,nci,pdf,https://www.cancer.gov/d1.pdf,pages,Rubs,NCI\n")
            with open(os.path.join(folder, "text", "d1.pages.json"), "w", encoding="utf-8") as handle:
                json.dump(doc("pages", [(20, PEPPER)]), handle)
            with contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()):
                hand.main(["extract", "--out", folder, "--books", books, "--host", "http://model:11434", "--model", "m", "--answer-within", "45"])
        self.assertEqual(gate_calls, [False])
        self.assertEqual(seen, {"asked": ("http://model:11434", "m", 45.0), "answers": True})

    def test_a_call_waits_for_a_free_host_and_is_then_asked(self):
        busy = [True, True, False]
        slept = []
        gate = hand.HostGate(lambda system, user: "answer", lambda: busy.pop(0), limit=600, poll=60, sleep=slept.append)
        self.assertEqual(gate("s", "u"), "answer")
        self.assertEqual((slept, gate.waited, gate.exhausted), ([60, 60], 120, False))

    def test_past_the_wait_limit_the_run_stops_asking_keeps_its_answers_and_a_later_run_asks_only_the_pages_missing(self):
        pages = [(20, PEPPER), (21, SALT), (22, SAGE)]
        asked, readings = [], []

        def host(system, text):
            asked.append(text)
            return rub_answer(text)

        def in_use():
            readings.append(len(asked))
            return len(asked) >= 1
        with tempfile.TemporaryDirectory() as folder:
            checkpoint = os.path.join(folder, "d1.answers.jsonl")
            gate = hand.HostGate(host, in_use, limit=120, poll=60, sleep=lambda s: None)
            first = hand.extract_document(doc("pages", pages), SOURCES["nci"], ask=hand.cookbook.resumable(gate, checkpoint, wait=0, sleep=lambda s: None, retry_timeouts=False))
            self.assertTrue(gate.exhausted)
            self.assertEqual([r["recipe"]["name"] for r in first.kept], ["Pepper Rub"], "staging checkpoint 6: NCI was given up whole on a busy host")
            self.assertEqual([(r["page"], r["reason"].split(":")[0]) for r in first.rejects], [(21, "model host busy"), (22, "model host busy")])
            self.assertEqual(len(readings), 4, "one free reading, then three busy ones within the 120 s limit; page 22 is not waited for again")
            self.assertEqual(hand.summarize({"nci": [("d1", first)]})["nci"]["droppedByReason"], {"model host busy": 2})
            free = hand.HostGate(host, lambda: False, limit=120, poll=60, sleep=lambda s: None)
            second = hand.extract_document(doc("pages", pages), SOURCES["nci"], ask=hand.cookbook.resumable(free, checkpoint, wait=0, sleep=lambda s: None, retry_timeouts=False))
        self.assertEqual([r["recipe"]["name"] for r in second.kept], ["Pepper Rub", "Salt Rub", "Sage Rub"])
        self.assertEqual([asked.count(t) for _, t in pages], [1, 1, 1], "an answered page is never asked again")


class OcrStopTests(unittest.TestCase):
    def setUp(self):
        self.folder = tempfile.mkdtemp()
        self.addCleanup(shutil.rmtree, self.folder)
        self.books = os.path.join(self.folder, "books.csv")
        self.store = hand.Store(os.path.join(self.folder, "work"))
        real = hand.have
        self.addCleanup(setattr, hand, "have", real)

    def listed(self, *rows):
        with open(self.books, "w", encoding="utf-8") as handle:
            handle.write("doc_id,source,kind,url,layout,title,attribution\n")
            for row in rows:
                handle.write(",".join(row) + "\n")

    def add(self, name, data):
        path = self.store.path(name)
        with open(path, "wb") as handle:
            handle.write(data)
        self.store.add(name, {"size": len(data), "sha256": hand.sha256_file(path), "type": "json" if name.endswith(".json") else "pdf"})

    def main(self, *argv):
        out, err = io.StringIO(), io.StringIO()
        with contextlib.redirect_stdout(out), contextlib.redirect_stderr(err):
            code = hand.main([*argv, "--out", self.store.out, "--books", self.books])
        return code, out.getvalue(), err.getvalue()

    def test_a_library_book_is_read_into_text_without_tesseract_or_poppler(self):
        hand.have = lambda tool: False
        self.listed(("loc-book1", "loc", "loc-item", "https://www.loc.gov/item/book1/", "book", "Book", "Library of Congress. Book"))
        self.add("loc-book1.item.json", json.dumps({"resources": [{"url": "https://www.loc.gov/resource/gdc.book1/"}]}).encode())
        self.add("loc-book1.text.json", json.dumps({"1": {"fulltext": ""}, "2": {"fulltext": "GINGERBREAD.\n\nOne cup of molasses."}}).encode())
        code, out, err = self.main("text")
        self.assertEqual(code, 0, "staging checkpoints 3 to 6 did not run the Library's books 'because tesseract is missing'")
        self.assertEqual(json.loads(out), {"documents": 1, "loc-blank": 1, "loc-ocr": 1})
        self.assertNotIn("tesseract", err)

    @unittest.skipUnless(POPPLER, "pdftotext and pdftoppm (poppler-utils) are not installed")
    def test_pdf_pages_with_no_text_layer_and_no_tesseract_stop_text_and_extract_naming_the_image(self):
        hand.have = lambda tool: tool != "tesseract"
        card = os.path.join(self.folder, "card.pdf")
        write_pdf(card, [CARD])
        width, height, gray = gray_of(card, self.folder)
        scan = os.path.join(self.folder, "scan.pdf")
        write_pdf(scan, [[(72, 700, 12, "A cover page with words on it for the text layer.")], ("image", width, height, gray)])
        self.listed(("nci-book", "nci", "pdf", "https://www.cancer.gov/book.pdf", "pages", "Book", "Adapted from National Cancer Institute"))
        with open(scan, "rb") as handle:
            self.add("nci-book.pdf", handle.read())
        code, out, err = self.main("text")
        self.assertEqual(code, hand.STOPPED, "staging checkpoints 3 to 6: 27 NCI pages were only counted needs-ocr")
        self.assertEqual(json.loads(out)["needs-ocr"], 1)
        self.assertIn("nci-book (1)", err)
        self.assertIn("nom-hand-import image", err)
        code, out, err = self.main("extract")
        self.assertEqual(code, hand.STOPPED)
        self.assertEqual(json.loads(out)["nci"]["unparsedDocuments"], 1)
        self.assertIn("nom-hand-import image", err)
        with open(os.path.join(self.store.out, "records", "nci-book.rejects.jsonl"), encoding="utf-8") as handle:
            self.assertTrue(json.loads(handle.readline())["reason"].startswith("needs OCR: 1 pages"))


class BoxTests(unittest.TestCase):
    def box(self, *argv, **env):
        base = {"PATH": os.environ.get("PATH", "/usr/bin:/bin"), "NOM_HAND_DRY_RUN": "1"}
        return subprocess.run(["bash", BOX, *argv], env={**base, **env}, capture_output=True, text=True)

    def test_every_step_runs_in_the_image_with_the_work_folder_api_and_model_from_the_environment(self):
        with tempfile.TemporaryDirectory() as work:
            books = os.path.join(work, "books.csv")
            with open(books, "w", encoding="utf-8") as handle:
                handle.write("doc_id,source,kind,url,layout,title,attribution\n")
            done = self.box("extract", "--books", books, "--wait-limit", "90", NOM_HAND_WORK=work, NOM_HAND_MODEL_HOST="http://model.test:11434",
                            NOM_API="http://api.test", NOM_API_KEY="s3cret-key-value")
            self.assertEqual(done.returncode, 0, done.stderr)
            self.assertNotIn("s3cret-key-value", done.stdout, "a secret is passed by name, never on the command line")
            tokens = shlex.split(done.stdout)
            self.assertEqual(tokens[:2], ["docker", "run"])
            at = tokens.index("nom-hand-import")
            options, tail = tokens[:at], tokens[at + 1:]
            self.assertIn(f"{os.path.realpath(work)}:/work", options)
            for name in ("NOM_API", "NOM_API_KEY", "NOM_API_TOKEN"):
                self.assertIn(["-e", name], [options[i:i + 2] for i in range(len(options) - 1)])
            self.assertIn("--read-only", options)
            self.assertEqual(tail, ["extract", "--books", books, "--wait-limit", "90", "--out", "/work", "--host", "http://model.test:11434",
                                    "--model", "qwen2.5:14b-instruct"])
            local = [os.path.realpath(work) if t == "/work" else t for t in tail]
            with contextlib.redirect_stdout(io.StringIO()):
                self.assertEqual(hand.main(local), 0, "the arguments the wrapper builds are the script's own")

    def test_each_step_of_the_script_is_a_step_of_the_wrapper_and_the_api_steps_need_no_work_folder(self):
        for step in ("fetch", "links", "text", "extract", "sample", "stage"):
            with tempfile.TemporaryDirectory() as work:
                done = self.box(step, NOM_HAND_WORK=work)
                self.assertEqual(done.returncode, 0, done.stderr)
                self.assertEqual(shlex.split(done.stdout)[-3:], [step, "--out", "/work"])
        for step in ("sources", "withhold", "release"):
            done = self.box(step, "--source-id", "va=25")
            self.assertEqual(done.returncode, 0, done.stderr)
            self.assertNotIn("/work", done.stdout)
        build = shlex.split(self.box("build", NOM_HAND_IMAGE="nom-hand-import:v0.3.130").stdout)
        self.assertEqual(build, ["docker", "build", "-f", os.path.join(os.path.realpath(OPS), "hand-import.Dockerfile"), "-t", "nom-hand-import:v0.3.130",
                                 os.path.realpath(OPS)])

    def test_a_missing_work_folder_an_out_folder_outside_it_and_an_unknown_step_are_refused(self):
        self.assertIn("NOM_HAND_WORK is not set", self.box("text").stderr)
        with tempfile.TemporaryDirectory() as work:
            outside = self.box("text", "--out", "/home/someone/hand-import", NOM_HAND_WORK=work)
            self.assertNotEqual(outside.returncode, 0)
            self.assertIn("not in the work folder", outside.stderr)
            kept = self.box("text", "--out", "/work/va", NOM_HAND_WORK=work)
            self.assertEqual(shlex.split(kept.stdout)[-3:], ["text", "--out", "/work/va"])
        self.assertNotEqual(self.box("purge").returncode, 0)

    def test_the_image_holds_the_open_source_tools_and_every_file_the_script_loads_and_nothing_secret(self):
        with open(DOCKERFILE, encoding="utf-8") as handle:
            dockerfile = handle.read()
        with open(os.path.join(OPS, "hand-import.py"), encoding="utf-8") as handle:
            loaded = re.findall(r'_load\("[^"]+", "([^"]+)"\)', handle.read())
        self.assertTrue(loaded)
        copied = " ".join(line for line in dockerfile.splitlines() if line.startswith("COPY"))
        for name in ["hand-import.py", *loaded, "hand-import/sources.csv", "hand-import/books.csv"]:
            self.assertIn(name, copied)
        for package in ("poppler-utils", "tesseract-ocr", "tesseract-ocr-eng"):
            self.assertIn(package, dockerfile)
        self.assertNotRegex(dockerfile, r"NOM_API|KEY|TOKEN|PASSWORD|\d+\.\d+\.\d+\.\d+:|\.lan\b|\.local\b")
        self.assertIn('ENTRYPOINT ["python3", "-I", "/app/ops/hand-import.py"]', dockerfile)

    def test_the_files_the_image_copies_run_on_their_own(self):
        with open(DOCKERFILE, encoding="utf-8") as handle:
            copies = [line.split()[1:] for line in handle if line.startswith("COPY")]
        with tempfile.TemporaryDirectory() as root:
            for parts in copies:
                *files, target = parts
                folder = os.path.join(root, target.lstrip("/"))
                os.makedirs(folder, exist_ok=True)
                for name in files:
                    shutil.copy(os.path.join(OPS, name), folder)
            work = os.path.join(root, "work")
            os.makedirs(work)
            done = subprocess.run([sys.executable, "-I", os.path.join(root, "app", "ops", "hand-import.py"), "extract", "--out", work],
                                  capture_output=True, text=True, cwd=work)
        self.assertEqual(done.returncode, 0, done.stderr)
        self.assertEqual(json.loads(done.stdout), {})


class StageTests(unittest.TestCase):
    def test_one_batch_per_document_under_its_hand_source_with_its_attribution_split_at_500(self):
        books = {"nci-a": hand.Book("nci-a", "nci", "pdf", "https://www.cancer.gov/a.pdf", "pages", "A", "Adapted from National Cancer Institute, A"),
                 "va-b": hand.Book("va-b", "va", "pdf", "https://www.nutrition.va.gov/b.pdf", "card", "B", "U.S. Department of Veterans Affairs")}
        records = [{"doc": "nci-a", "recipe": {"name": f"r{i}"}} for i in range(501)] + [{"doc": "va-b", "recipe": {"name": "b"}}]
        planned = hand.batches(records, books, {"nci": 41, "va": 42})
        self.assertEqual([(d, len(b["recipes"]), b["sourceId"]) for d, b in planned], [("nci-a", 500, 41), ("nci-a", 1, 41), ("va-b", 1, 42)])
        self.assertEqual(planned[0][1]["sourceAttribution"], "Adapted from National Cancer Institute, A")
        self.assertNotIn("publicDomain", planned[0][1])
        with self.assertRaises(ValueError):
            hand.batches(records, books, {"nci": 41})

    def test_the_judged_sample_is_seeded_and_carries_the_page_text(self):
        records = [{"doc": "d1", "source": "nci", "pages": [20], "recipe": {"name": f"r{i}", "sourceUrl": "u", "ingredients": [{"rawLine": "1 egg"}], "steps": []}}
                   for i in range(30)]
        texts = {"d1": {"pages": [{"n": 20, "text": "page twenty"}]}}
        first = hand.judged_sample(records, texts, 5, 1)
        self.assertEqual([r["name"] for r in first], [r["name"] for r in hand.judged_sample(records, texts, 5, 1)])
        self.assertEqual(len(first), 5)
        self.assertEqual(first[0]["pageText"], "page twenty")
        self.assertEqual(first[0]["verdict"], "")


if __name__ == "__main__":
    unittest.main()

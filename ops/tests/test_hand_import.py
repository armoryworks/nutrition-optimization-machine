"""
Tests for the hand import of the four approved PDF and scanned sources: the listed documents and
their hosts, the polite resumable fetch and its verification, page text from a text PDF page and a
scanned page made here, the rule readers of a recipe card and a recipe page, the model extraction of a
book with its number check, the source links by page, and the staged-import batches.
No network: the sites and the model are recorded stand-ins. pdftotext and pdftoppm (poppler) are
needed for the PDF tests, tesseract for the one real OCR test; each is skipped without its tool.
Run: python3 -m unittest discover -s ops/tests
"""

import importlib.util
import json
import os
import shutil
import subprocess
import sys
import tempfile
import unittest

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
    allowed = {"www.nutrition.va.gov": 5.0, "www.loc.gov": 6.0, "tile.loc.gov": 6.0}
    allowed.update(delays)
    return hand.Fetcher(allowed, opener=site, clock=clock.time, sleep=clock.sleep)


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
            self.assertEqual([(p["n"], p["method"]) for p in doc["pages"]], [(1, "needs-ocr"), (2, "loc-ocr")])

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
                         [("Hot 'n Spicy Seasoning", [20], "https://www.cancer.gov/book.pdf#page=20")])
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
        self.assertEqual(links["Sponge Cake"], ([51], "https://www.loc.gov/resource/gdc.book1/?sp=51&st=text"))
        self.assertEqual(links["Gingerbread"], ([50], "https://www.loc.gov/resource/gdc.book1/?sp=50&st=text&q=Gingerbread"))
        self.assertEqual(links["Corn Muffins"], ([50], "https://www.loc.gov/resource/gdc.book1/?sp=50&st=text&q=Corn%20Muffins"))
        self.assertEqual(result.rejects, [])

    def test_a_book_needs_the_model_host(self):
        result = hand.extract_document(doc("book", [(1, "GINGERBREAD\n\n1 cup molasses")]), SOURCES["loc"])
        self.assertEqual(result.unparsed[0]["reason"], "a book needs the model host (--host, --model)")

    def test_running_heads_and_page_numbers_are_taken_off_the_page_edges(self):
        pages = [f"{n} HOME COOK BOOK.\n\nRECIPE {n}\n\nOne cup of milk.\n\n{n}" for n in range(20, 24)]
        stripped = hand.strip_running_heads(pages)
        self.assertTrue(all("HOME COOK BOOK" not in p for p in stripped))
        self.assertTrue(all(p.strip().endswith("One cup of milk.") for p in stripped))
        self.assertIn("RECIPE 21", stripped[1])


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

# Hand import of PDF and scanned recipe sources

On 2026-11-21 the owner approved four recipe sources that the scraper cannot read, because they
are PDFs, scanned books or plain pages with no schema.org Recipe:

| Source | Host(s) | Documents listed | Licence |
|---|---|---|---|
| VA Nutrition and Food Services (`va`) | `www.nutrition.va.gov` | 338 recipe-card PDFs, one recipe each | U.S. federal work |
| NHLBI Healthy Eating Recipes (`nhlbi`) | `www.nhlbi.nih.gov` | 54 recipe pages, one recipe each | stated public domain |
| Library of Congress, Selected Digitized Books (`loc`) | `www.loc.gov`, `tile.loc.gov` | 98 cookbooks of 1876 to 1923 | each item's rights field: public domain |
| NCI "Down Home Healthy Cooking" (`nci`) | `www.cancer.gov` | one 52-page PDF | NCI text is free of copyright; credit NCI |

They come in by hand, through `ops/hand-import.py`, never through nom-api's scraper path. The
script lives in `ops/` because it fetches; nom-api never fetches a recipe site.

## Files

- `ops/hand-import/sources.csv`: each source's domain, the hosts its documents may be fetched
  from, its licence kind, the licence statement as the source words it, where it was found, the
  delay between requests, and the site name a record carries. These are the facts an admin records
  for the hand source.
- `ops/hand-import/books.csv`: every document the script may fetch (`doc_id`, `source`, `kind`
  `pdf`/`html`/`loc-item`/`index`, `url`, `layout`, `title`, and the attribution line the staged
  import gives its recipes). `index` rows are the listing pages the `links` step reads to find new
  documents; they are never imported. The host determinations are in `crosscheck-sources.md`.

## Steps

1. **fetch** (`--out` a folder outside the repository; the script refuses a folder inside it).
   Only listed documents, only from their source's hosts. robots.txt is read once per host per run:
   404 allows all, 401/403 refuses all, any other failure stops that host for the run. Requests go
   one at a time, spaced by the larger of the source's delay and robots.txt's `crawl-delay`; the hosts
   of one source share that spacing (the Library's `www.loc.gov` and `tile.loc.gov` are paced as one).
   A redirect is followed only to a host of the document's own source and an allowed path. A Library of Congress item is
   fetched as its JSON record first; it is taken only when its rights field states the public domain,
   it is dated 1930 or earlier and is in English, and then its OCR text (`fulltext_derivative` on
   `tile.loc.gov`, one JSON file of page texts keyed by page sequence) is the document. A download
   is written to `<name>.part`, resumed with a `Range` request on the next run, and verified: the
   length against `Content-Length`, a PDF's `%PDF-` start and `%%EOF` end, an HTML page's closing
   tag, a JSON file's parse, and the listed kind. Each verified file is added to `manifest.jsonl`
   (URL, final URL, size, type, SHA-256, time) and `SHA256SUMS`; a listed file whose sum still
   matches is not fetched again.
2. **links**: reads the listed `index` pages and prints `books.csv` rows for documents they link to
   that are not listed yet (PDFs, and for NHLBI the pages below the listing). The operator reviews
   them before adding them. VA files some recipes in several categories under different paths; the
   listed set keeps one row per title.
3. **text**: one `text/<doc>.pages.json` per document with every page's number, text and method.
   PDFs: `pdftotext -bbox-layout` (poppler); a page's blocks are read top to bottom, and where the
   page has two columns (three or more blocks sharing a left edge in the middle half of the page)
   the left column is read before the right one between full-width blocks, so a recipe card's
   ingredients do not interleave with its directions. A page with fewer than 20 letters and digits
   has no text layer: it is rendered with `pdftoppm -r 300 -gray` and read by `tesseract -l eng`;
   without tesseract it is marked `needs-ocr` and counted. Library of Congress books use the
   Library's own OCR, page by page. HTML pages: the `<main>` content without scripts, navigation and
   footers, and the page title before its " | site" part. For books, running heads and page numbers
   on a page's first or last line are taken off.
4. **extract**: `records/<doc>.records.jsonl` and `records/<doc>.rejects.jsonl`, and
   `extract-summary.json` with, per source, documents, pages, pages needing OCR, records kept,
   records dropped by reason and documents not parsed.
   - `card` (VA): read by rule: title (the listing's title), `Prep | Cook | Total`, `Yield`, the
     Ingredients section (a text block starts a line; wrapped parts are joined) and the Directions
     section (numbered steps take their block; the first unnumbered block after them, a note, ends
     them), up to Recipe Notes and the nutrition line.
   - `html` (NHLBI): the title, the sentence under it as the description, the Ingredients and
     Directions blocks, and the labelled times and yield.
   - `book` (Library of Congress): `ops/cookbook-extract.py`'s model extraction over chunks cut at
     recipe titles across pages, and its rules (`check_record`: every ingredient line and step
     found in the recipe's own text with all its numbers, every line of an ingredient list
     extracted). Needs `--host` and `--model` (reader A of the roster, `qwen2.5:14b-instruct`).
   - `pages` (NCI, one recipe to a page): the same model extraction page by page; the record must
     pass the number check, and every line of the page that starts with an amount (a bare step
     number aside) must lie inside one of its ingredient lines or a step, so a wrapped line counts
     once and no listed ingredient is left out.

   Every record of every layout then passes the **number check**: the cookbook extraction's
   fidelity check on each ingredient line (digits) and each step (number words too) against the
   page text, and every number of the yield and times must stand in the page. A record that fails
   any check is dropped and counted with its reason, never repaired, by a model or otherwise. The
   model writes no amount: every amount it returns must already stand in the page.

   Nothing else is taken: no image (licences exclude some), and none of the sources' nutrition
   figures (NOM computes nutrition per 100 g from its own catalog, and a source's per-serving figure
   is never stored).
5. **sample**: a seeded random sample per source as CSV with each record, its page link and its
   page text, for a person to judge (`verdict`, `note` left blank).
6. **sources** (with `NOM_API` and `NOM_API_KEY`): the four `POST /api/ScrapingSource/hand`
   bodies from `sources.csv`; `--apply` records them and prints each source's id.
7. **stage**: one `POST /api/StagedImport` body per document (split at 500 recipes) with the
   source's `sourceId` (`--source-id va=31 ...`), the document's attribution line as
   `sourceAttribution`, and `importKeywordsAsTags` false. Without `--apply` it only writes the
   bodies to `staged/` and counts them; with `--apply` it posts them, paced like
   `ops/recipe-source-attach.py` (40 calls in 10 s, 80 a minute, 900 an hour, 429 waited out), and
   logs every answer (`imported`, `skippedDuplicates`, `refused`, failures).
8. **withhold** / **release** (`--source-id`, dry run unless `--apply`): the undo. Withholding a
   hand source's recipes (`POST /api/ScrapingSource/{id}/withhold-recipes`) takes every recipe whose
   URL is on its domain out of search, suggestions, meal plans and cookbooks, journaled; release or
   the journal's undo brings them back.

## The source link and the stored source

A record's `sourceUrl` is the document's URL. For a document that holds more than one recipe it
names the page and the recipe: a PDF's `#page=N&search="<name>"` (the PDF open parameters), a
Library of Congress page `https://www.loc.gov/resource/<id>/?sp=N&st=text&q=<name>`. The name is
always given, so a record's link depends only on its own page and name, never on which other records
a run keeps on that page. nom-api's URL normalization keeps a fragment that starts with
`#page=` (and still drops every other fragment), so the recipes of one PDF are not taken for
duplicates, and a rerun of the same files imports nothing twice. The recipe links to the page it
came from, and withholding by domain reaches it.

Because the record has a URL, the staged import stores it as the recipe's `scraper-recipe`
document (`recipe.ScrapedDocument`, with its checksum), which is the source the reprocess path
reads (`RecipeReprocessService.SourceAsync`). The downloaded document, its SHA-256 and the page
texts stay in the download folder; each record line carries the document's SHA-256 and pages.

## Rules the imported recipes go through

The staged import is the only way in, so a hand-imported recipe goes through everything any
staged recipe goes through: the staged-import gate (an approved, not paused hand source; the
licence from its record, never the caller's flag), NOM's line builder (`IngredientLineParser`), the
wording table and link gate that decide every imported line's food, vetting (`RequiresRevision` with its issues, or the curation
queue), the diet gates (alcohol, restrictions), the reprocess detector and lane, and duplicate
retirement. The licence of all four records is `public-domain`, so the recipes are imported as
`PublicDomain` with `ContainsSourceProse` false, exactly like the cookbook imports: the prose
re-check and the clean-room prose lane, which take recipes of unknown licence, do not rewrite them.
Withholding by source applies to them like any other source.

## What must be installed where the script runs

- Python 3.10 or later (standard library only).
- `poppler-utils` (`pdftotext`, `pdftoppm`), GPL: for every PDF.
- `tesseract-ocr` and `tesseract-ocr-eng`, Apache-2.0: for PDF pages with no text layer (NCI's
  photo pages; any scanned VA card). Without it those pages are counted as `needs-ocr`.
- For the `book` and `pages` layouts, an Ollama host serving `qwen2.5:14b-instruct` (Apache-2.0).

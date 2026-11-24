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
script lives in `ops/` because it fetches; nom-api never fetches a recipe site. It is run from the
application server, in the `nom-hand-import` image, staging first (see "Where it runs" below).

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
   has no text layer: it is rendered with `pdftoppm -r 300 -gray` and read by `tesseract -l eng`.
   Where tesseract is not installed, such a page is marked `needs-ocr`, the step still writes every
   document's text, and then **stops** (exit 3) with a message that names each document, its pages
   without text and the `nom-hand-import` image that reads them; `extract` does not extract a document
   with a `needs-ocr` page (counted unparsed, `needs OCR: ...`) and stops the same way. A page that
   needs OCR is never left as a count the operator can miss (staging checkpoints 3 to 6 carried NCI's
   27 photo pages as a `needs-ocr` count). Library of Congress books use the Library's own OCR text,
   page by page, and need neither tesseract nor poppler: a page the Library's text leaves blank (a
   plate, a blank leaf) is marked `loc-blank`, not `needs-ocr` (checkpoints 3 to 6 skipped the books
   "because tesseract is missing", which they never used). HTML pages: the `<main>` content without scripts, navigation and
   footers, and the page title before its " | site" part. For books, running heads and page numbers
   on a page's first or last line are taken off.
4. **extract**: `records/<doc>.records.jsonl` and `records/<doc>.rejects.jsonl`, and
   `extract-summary.json` with, per source, documents, pages, pages needing OCR, records kept,
   records dropped by reason and documents not parsed.
   - `card` (VA): read by rule: the title the card prints (the lines above its first header line,
     `Prep:` or `Yield:`, without a template's "Recipe Title" placeholder; the listing's title only
     when the card prints none: staging checkpoint 4, P28, 53 of 337 names came from the index list),
     each named time of the header (`Prep`, `Cook`, `Total` in any order; a `Chill` or `Inactive`
     field is part of the total and is not kept on its own: P27, 47 of 337 cards lost every time),
     `Yield`, the Ingredients section (a text block starts a line; wrapped parts are joined, and a
     line that ends with a word that cannot end it, "into", "or", "to", "about", "of", "with",
     "for", "plus", "from", a dash or an open bracket, takes the next line even across a block break
     and when the next starts with an amount: staging checkpoint 3, N29, 11 of 337 VA recipes; a line
     that ends with "if", "when", "until" or "than", or with a word that names no food alone ("or
     plain" / "Greek yogurt"; after a list comma only on a line with no amount, "Chopped basil,
     grated" / "Parmesan cheese"), takes the next line unless it starts with an amount, and a line
     that is only a bracket, "(optional)", always joins the line before: P26, 5 of 337) and the Directions
     section (numbered steps take their block; a short line before a step numbered 1 heads a part of
     the method, and that part's steps keep it as their `section`, which the recipe shows as
     "[Make Dip or Dressing] To make ranch dip, ..."; any other unnumbered block after them, a note,
     ends them: staging checkpoint 6, R20, `va-ranch-seasoning-mix` kept 2 of its 3 steps and dropped
     the part "Make Dip or Dressing"), up to Recipe Notes and the nutrition line. A card's record
     that keeps fewer steps than its method numbers (step-numbered lines from the Directions heading
     to the notes or nutrition line) is dropped by the number check, `number check: the record keeps
     N of the M numbered steps ...`: the check otherwise finds only what a record holds in its page,
     so it passed the ranch card. A notes heading is only a line that is the
     heading alone ("Recipe Notes", "Notes:", "Tips"); a nutrition heading may carry its figures on
     the line but is never followed by a closing bracket. A card whose kept ingredient lines are fewer
     than half the lines of its list (from the Ingredients heading to the next section heading) that
     start with an amount is dropped by the reader's own check, `reader check: kept N of the M ...`
     (staging checkpoint 5, Q23: the arepas card's wrapped "Notes)" of "see Recipe / Notes)" ended
     its list, and the record kept 1 of 12 lines and passed the number check). A line after "de", "del",
     "di", "du", "des" or "della" continues it ("Pico de" / "Gallo"), and a card with a line that starts a
     new ingredient inside a text block without an amount (a wrap the joiner did not join) is dropped by
     the same check, `reader check: a line inside an ingredient block ...` (review of Q23: the arepas card
     still split its toppings line).
   - `html` (NHLBI): the title, the sentence under it as the description, the Ingredients and
     Directions blocks, and the labelled times and yield. The directions end at the page furniture
     after them, the "Recipe Video" widget label included (staging checkpoint 3, N30, 7 of 54).
   - `book` (Library of Congress): `ops/cookbook-extract.py`'s model extraction over chunks cut at
     recipe titles across pages, and its rules (`check_record`: every ingredient line and step
     found in the recipe's own text with all its numbers, every line of an ingredient list
     extracted). Needs `--host` and `--model` (reader A of the roster, `qwen2.5:14b-instruct`).
   - `pages` (NCI, one recipe to a page): the same model extraction page by page; the record must
     pass the number check, and every line of the page that starts with an amount (a bare step
     number aside) must lie inside one of its ingredient lines or a step, so a wrapped line counts
     once and no listed ingredient is left out. A returned name in letter-spaced type ("C AT F I S H
     STEW with RICE") is closed up by rule: a run of three or more tokens of one or two letters,
     mostly single, is cut into the fewest words of the page's own text and a short list of cookbook
     title words, and kept as one word when it cannot be (P34).
   - Model calls (`book`, `pages`): `--timeout` (seconds per call, default 600) and `--retries`
     (default 2, waits doubling from 30 s). Every readable answer is appended to
     `records/<doc>.answers.jsonl` the moment it arrives, keyed by the model, the prompt and the
     text, and read back on the next run with the same `--model`, so a page or chunk is never asked
     twice; an unreadable answer is not kept, so it is asked again, and another model's answers are
     never reused; a call that still fails drops only its page or chunk, counted as
     `model call failed`, and the run goes on and writes every record it has (staging checkpoint 3,
     N31: one call past 600 s ended the NCI run and lost everything). In `pages` mode a call that
     times out is not asked again in the run: one timeout drops that page, counted as
     `model call timed out`, and the next run asks it again (staging checkpoint 4, P34: one page
     that never answered held the NCI run for two 1,800 s timeouts and would have held it a third);
     `--retries` still covers a refused connection or another failure there, and every failure in
     `book` mode. Run `extract` again to ask only
     what is missing.
   - Waiting for a shared model host: before every call that is not already answered the host's
     `/api/ps` is read twice, `--probe` seconds apart (default 2); the host is in use by another job
     when a model loads between the readings or a loaded model's `expires_at` moves (it moves with
     the clock while a model's first request after its load is in flight: staging checkpoint 6,
     section 28). Ollama sets `expires_at` only when a model's last request finishes, so a request
     on a model already loaded leaves both readings the same (checkpoint 6, 10:42Z: `qwen2.5vl:7b`
     loaded and in use); when the readings show nothing, the host is asked a one-token request to
     the same model with the real calls' context size (so the model is not reloaded), and one not
     answered within `--answer-within` seconds (default 30) counts as in use and is abandoned
     (review of ck14-hand-import-box, CK14-1). A host that does not answer counts as in use. The
     first call of a run may also read as in use while the model loads cold; it then waits one
     `--wait-poll`. While it is in use the call waits, `--wait-poll` seconds at a time
     (default 60), out of one `--wait-limit` for the whole run (minutes, default 120). Past the limit
     nothing more is asked: every page or chunk not asked is counted `model host busy`, every record
     already answered is written, and the run stops with exit 3 and a message to run `extract` again,
     which asks only what is missing. A busy host never gives up a whole source (checkpoint 6 left
     NCI unextracted when the host was busy at three checks). `ops/cookbook-extract.py` takes the same `--timeout`, `--retries` and
     `--checkpoint` (default `<out>.answers.jsonl`) and counts `failedChunks`.

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

## Where it runs

The hand import runs on the application server as a container, and nothing is installed on any
host. `ops/hand-import.Dockerfile` builds the `nom-hand-import` image from the Python slim image
(Debian) with `poppler-utils` (`pdftotext`, `pdftoppm`, GPL) and `tesseract-ocr` with
`tesseract-ocr-eng` (Apache-2.0); the script needs only Python's standard library. The image holds
the script, the two modules it loads (`cookbook-extract.py`, `recipe-source-attach.py`) and the two
lists, and nothing else: no secret, no host name, no address. One image serves staging and
production; only the environment of a run differs.

`ops/hand-import-box.sh` runs every step in it (`fetch`, `links`, `text`, `extract`, `sample`,
`sources`, `stage`, `withhold`, `release`), and `build` builds it. It reads at run time:

| Variable | Use |
|---|---|
| `NOM_HAND_WORK` | the work folder on the server, mounted at `/work` (required for every step with `--out`; `--out` defaults to `/work` and must lie under it) |
| `NOM_HAND_MODEL_HOST`, `NOM_HAND_MODEL` | the model host and model for `extract`, passed as `--host` and `--model` (model default `qwen2.5:14b-instruct`) |
| `NOM_API`, `NOM_API_KEY`, `NOM_API_TOKEN` | the NOM API of the environment being imported into, passed into the container by name, never on the command line |
| `NOM_HAND_NETWORK` | a docker network for the container (`host` when `NOM_API` is a loopback address) |
| `NOM_HAND_IMAGE` | the image tag (default `nom-hand-import`) |
| `NOM_HAND_DRY_RUN=1` | print the `docker` command instead of running it |

The container runs as the calling user, read-only, with a private `/tmp`, no capabilities and no new
privileges, and is removed when the step ends. The import is run from the server, staging first;
production only in the one rollout, with the same image and the same steps.

For the `book` and `pages` layouts the model host serves `qwen2.5:14b-instruct` (Apache-2.0).

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
  delay between requests, the site name a record carries, and the hosts whose page images may be
  read for their text (`image_hosts`: `tile.loc.gov` for the Library, under its determination in
  `crosscheck-sources.md`; empty for the others). These are the facts an admin records for the hand
  source.
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
   page by page, and need neither tesseract nor poppler for it (only a page image fetched by
   `images`, below, is read by tesseract): a page the Library's text leaves blank (a
   plate, a blank leaf) is marked `loc-blank`, not `needs-ocr` (checkpoints 3 to 6 skipped the books
   "because tesseract is missing", which they never used). HTML pages: the `<main>` content without scripts, navigation and
   footers, and the page title before its " | site" part. For books, running heads and page numbers
   on a page's first or last line are taken off.

   **Readability of the Library's text** (staging checkpoint 7, S22). The Library's own OCR garbles
   the ingredient tables old cookbooks set with dot leaders ("Corn meal . . . . 1 pint" came out as
   "ELI aa a as I pint"); in Lizzie's cook book (1891) 13 of 14 recipes were dropped and the one kept
   was that garbage, copied verbatim, which the number check passed. So every line is now read
   against a word list (SCOWL's `american-english`, Debian's `wamerican`, installed in the image;
   `--words` names another): a line is readable when, dot leaders and numbers aside, three in four
   of its letter tokens are known words (lower-case entries only, so a name such as "Eli" is no word;
   a plural, "un" before a known word and two known words run together count; a short list of old
   cookery words is added: saleratus, pearlash, graham, teaspoonful ...), it holds no token that
   mixes figures and letters ("0Ut", "1ae"; an ordinal or a unit glued to its number aside) and no
   stray symbol ("|", "§"), and it has a known word that names something besides a measure. With the
   word list, `text` marks a Library page `needsImage` when three or more of its short lines, and
   three in ten or more, fail; it prints the count as `pagesNeedingImage`. Without a word list it
   says so and marks nothing.

   **images** (Library books only), within the `tile.loc.gov` page-image determination of
   2026-11-25 (`crosscheck-sources.md`); everything outside it is refused and named in the report:
   - only an item already taken under the Library rule: its JSON record and OCR text fetched and
     verified, and the record read again (rights field states the public domain, dated 1930 or
     earlier, English);
   - only a page whose Library text fails the readability test, tested again on the Library's own
     text with the word list when the step runs (`--words`; without a word list nothing is fetched),
     whatever the page's text file says; a page whose image was already read (`imageText` stored) is
     never fetched again (`alreadyRead`);
   - only from a host in the source's `image_hosts` column of `sources.csv` that is also one of its
     hosts (`tile.loc.gov` for `loc`, no other), at the URL the item's record lists for that page
     (`resources[0].files`, one list of variants per page: the largest JPEG at least 1,500 pixels
     wide, else the JPEG 2000 master, else the largest JPEG), a redirect followed only to such a host;
   - under `robots.txt` as read at the start of the run (a disallowed image, or a robots file that
     cannot be read, is refused) and at the source's 6 s pacing shared with `www.loc.gov`, or the
     larger `crawl-delay`;
   - verified (a JPEG's start and end markers, a JPEG 2000 signature; anything else is refused and
     removed) and listed with its SHA-256 as `docs/<doc>.p<NNNN>.jpg` until it is read. An image is
     never resumed: a download that fails deletes its `.part`, and any part of a page image a stopped
     run left (whatever its book) is deleted at the start of the next `images` or `text` run
     (`partialDeleted`, `partialImagesDeleted`).
   `--limit` caps the images of a run. Then `text` runs again in the image and reads each fetched
   image (below); the page stores that reading as `imageText`, and once the page's text file is
   written **the image is deleted**: it leaves `SHA256SUMS`, `manifest.jsonl` records the deletion
   (`deleted`, its sum, the reason), and `text` prints `imagesDeleted`. A later `text` run takes the
   stored reading for a page whose image is gone, and always keeps it on the page: run without a word
   list, the page keeps the earlier run's choice of text; under a word list by which the page no
   longer fails, the reading stays beside the Library's text. So the image is never fetched again. An image is read for its text only: never a
   recipe's image, never staged, served or sent anywhere. A fetched image that cannot be read where
   `text` runs (no tesseract) is kept and stops the run (exit 3) with the image's name.

   **Reading a page image** (`tesseract_image`). Read as one block of text (`--psm 6`, the first
   design), an ingredient table set with dot leaders came back with the leaders read as letters and
   figures glued to the name and the amount, and with numbers the page does not print: on the four
   test pages (below) "½ teaspoon" read as "4 teaspoon", "2 cups" as "& CUPS", and no recipe was read
   by rule. So the image is read for its words, their boxes and their confidence (`--psm 11`, TSV),
   and `table_text` sets them back into lines:
   - words are grouped into rows by their height on the page;
   - a **table row** starts in the left quarter of the text, reaches past its middle, and after its
     name holds either a gap wider than two letters (leaders not read) or words read with
     confidence below the bar (leaders read as dots, figures, stray letters) followed by at most
     three confident words. It is written `name . . . . . . amount`, which the rule reader reads
     amount first. The name is the row's first run of words read with confidence 90 or above; the
     amount its last run of such words, right of the middle. The amount is kept only when it is one
     number (a whole number, a fraction sign or both) and at most one word; otherwise, and when the
     name looks cut by the leaders ("crumbs...."), the row gets the unread mark (`{unread}`) in
     place of what was not read. A short line that starts where a neighbouring table row starts and
     ends left of the middle (a row whose amount was not read at all) gets it too;
   - any other row is its words, a word read below the bar replaced by the mark where it may be or
     cut an amount: it holds a figure or a sign that is no letter or punctuation ("%", "Y%"), stands
     next to a word with a figure ("1 % cups flour"), or starts the line; a blank line goes
     before and after a centred title, before an indented first line of a paragraph and at a wide
     gap, so the rule reader finds titles and steps.
   A recipe holding the mark is `unreadable` (`unreadable text: the page image was read, but not
   every amount with confidence`): never kept by rule and never sent to the model. So a fraction
   tesseract reads as "%" or "Y%", or a figure below the bar, never becomes a number the page does
   not hold; the recipe is lost instead.

   **Since staging checkpoint 8 (T18)** the Library proof kept one wrong amount from an image
   (Lizzie's cook book p. 99, `Butter . . . 2 Cup` where the page prints ½ cup: a fraction read as a
   plain figure at the bar), took index numbers from the other column and left an old-style "1" read
   as "I" unmarked. So:
   - **every amount the reading keeps is read again** (`confirmed`, `cell_reader`): the cell (the
     amount's words) is cropped from the page image with half a line of margin, enlarged three times
     (`CELL_SCALE`) and read by tesseract as one line (`--psm 7`); the amount is written only when
     that reading starts with the same number. A fraction sign, "%", "1/2", another figure or no
     reading at all puts the mark in its place. Cropping needs Pillow (HPND, OSI-approved), which the
     image installs (`pillow==11.0.0`); without it no cell is read again and no amount is kept;
   - **a lone figure as wide as a fraction sign** is never written: a printed figure is about half as
     wide as the line is high, a half about as wide as high, so a single figure whose box is at least
     `FRACTION_WIDTH` (0.8) of its height carries the mark whatever the readings say;
   - in a line set as prose, **an "I", "l" or "|" before a measure word** ("Sweet milk, I cup") and a
     figure before a measure word that its second reading does not confirm carry the mark;
   - **a two-column row is read as two rows** (`_columns`): at a gap wider than three letters across
     the middle, after which a name in capitals ends in a page number, so an index name never takes
     the other column's number;
   - **a page whose image reading names its lines replaces its garbled Library text**: a line that
     fails the readability test only because it carries the mark is counted `marked`, not garbled
     (`page_reading`), and the image's reading is the page's text when it has fewer garbled lines
     than the Library's (p. 25 kept the Library's garbled table though the image read every name). A
     marked recipe on such a page is still never kept by rule or sent to the model.
   With tesseract 5.3.4 on the four test pages every amount the reading keeps is still the printed
   one, and SEASONING is still kept by rule; the residual risk (a fraction read twice, at two
   resolutions, as the same plain figure, and narrow enough to pass the width rule) is measured by the
   next one-book proof.

   **What the test pages show** (`ops/tests/fixtures/hand-import/table-page-*.jpg`, made by
   `table-pages.py` from NCI's public-domain text set as old-book dot-leader tables at 300 dpi,
   turned, blurred and speckled; nothing taken from the Library; tesseract 5.3.4): of 29 table rows,
   14 amounts are read and all 14 are the printed amount; the 15 others (every fraction, "1 lb" at
   confidence 87, an "8" tesseract did not see) carry the mark. Of five recipes, the one whose table
   holds only whole numbers is kept by rule exactly; the four with a fraction or a row not read are
   unreadable. The page's own garbled Library text gives way to the image's reading, and nothing
   wrong is kept. Real scans may read better or worse: the one-book proof below measures them.

4. **extract**: `records/<doc>.records.jsonl` and `records/<doc>.rejects.jsonl`, and
   `extract-summary.json` with, per source, documents, pages, pages needing OCR, records kept,
   records dropped by reason and documents not parsed; for books also the records kept by rule, the
   model calls made, the model calls still left (in all and per document) and the unreadable pages.
   Every book also gets `records/<doc>.status.json` (its share read by rule, records kept by rule,
   model calls made and left, unreadable pages, done). **plan** prints and writes
   `extract-plan.json` without asking the model: for each Library book what the rules read, the
   model calls it needs and the calls still left with `--model` (answers already kept count as
   made), in the order `extract` takes the books: the books the rules read best first, then the
   fewest calls left.
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
   - `book` (Library of Congress): **rules first, the model only for the rest** (staging checkpoint
     7, S22: the 98 books were about 13,000 model chunks, days on a shared host).
     `ops/cookbook-extract.py`'s `read_by_rule` cuts the book's text at its recipe titles (a title
     line that fails the readability test is no title, so a garbled table row does not cut a recipe)
     and reads each recipe:
     - **rule**: old cookbooks set a recipe in a regular shape, and the rules split it: the title
       line; the ingredient list (short lines that start with an amount, a line in lower case or a
       bracket continuing the line before, or dot-leader rows, read amount first: "Corn meal . . .
       1 pint" is "1 pint Corn meal"); then the method in prose, one step per paragraph, a word broken
       at a line end joined (without its hyphen when the joined word is a known word, "beat-" / "en"
       is "beaten"; with it otherwise, "well-" / "beaten" is "well-beaten"). The record then passes
       `check_record` exactly as a model's record does, the readability test included; a recipe the
       rules split that fails a check goes to the model;
     - **unreadable**: three or more of the recipe's short lines, and three in ten or more, fail the
       readability test: it is never sent to the model (its readings were its own: "2 quarts of
       flour", a "walnut" the page does not print), it is counted `unreadable text: the page image is
       needed`, and its pages are the book's `unreadablePages`;
     - **none**: no line starts with an amount and no measure is named (a preface), or an index or
       table of contents (five or more dot-leader rows nearly all ending in a page number, no
       sentence): nothing is asked;
     - **model**: anything else (a recipe told in prose, "One cup of molasses, one cup of sugar ...",
       a list inside the method, a recipe with no title). Only these go to the model, in chunks of
       neighbouring model recipes cut at recipe titles (at most 4,000 characters unless one recipe is
       longer; one longer than 12,000 cut at paragraph ends), and every record passes `check_record`
       with the readability test.
     Without `--host` a book keeps what the rules read and counts the rest as model calls left (and
     unparsed, `a book needs the model host`). Needs `--host` and `--model` for the rest (reader A of
     the roster, `qwen2.5:14b-instruct`).
   - `pages` (NCI, one recipe to a page): the same model extraction page by page; the record must
     pass the number check and the readability test, and every line of the page that starts with an
     amount (a bare step number and the title's own lines aside: S23, "2 0 - M I N U T E" was read as
     an ingredient line the record left out) must lie inside one of its ingredient lines or a step, so
     a wrapped line counts once and no listed ingredient is left out. Since staging checkpoint 8 (T19:
     2 of 14 kept records wrong) a list heading in capitals the model returns as a line ("DRESSING",
     "FOR THE TOPPING:") is no ingredient line, a sidebar tip set after its icon letter ("Z To reduce
     sodium, ...": one capital other than A or I, then a capitalised word) is no step, and a word the
     page breaks with a soft hyphen comes back whole ("min–utes" is "minutes" where the joined word is
     known and its halves are not both words; the page text is read the same way for the number
     check). **The name is the title the
     page prints, read by rule** (staging checkpoint 7, S23: the model returned "MIXED DEGREE GREENS"
     for MIXED GREENS and lost "Breast" on page 13): a run of short lines in capitals (the page's
     INGREDIENTS and NUTRITION CONTENT headings aside; a run followed by an ingredient line is a
     heading of the list, "DRESSING", "TOPPING"; a run in spaced letters before any other; "and",
     "with", "n" may stay in lower case), with the line after it when that starts with "with"
     ("BAKED FRIED CHICKEN BREAST with MIXED VEGETABLES"). The PDF's text layer gives a spaced title
     as overlapping pieces ("M I XE" / "XED" / "D G R E E N S", "BAK" / "BA" / "K E D"): the pieces
     are closed up, a piece of three letters or fewer that repeats the end of what came before is
     dropped, an overlap is taken once, and the letters are cut into the fewest words of the page's
     own text (its title lines aside), the cookbook title words and the word list's words of three
     letters or more, a letter the layer doubles taken once ("SKIIL LET" is SKILLET). A title that
     cannot be cut into known words is not read; the model's name is then kept only when each of its
     words stands on the page (a made-up word drops the record). A returned name in letter-spaced
     type is otherwise closed up as before (P34). **The yield is the page's "Makes ..." line**
     ("Makes 6 servings" under the nutrition box is `recipeYield` "6 servings" and `recipeServings`
     6; "Makes about ⅓ cup." is `recipeYield` "about ⅓ cup"), and a step that is only that sentence
     is no step (S23: page 21 kept it as step 3). The amount ends with its sentence: a step that
     opens with the yield and goes on ("Makes 12 muffins. Bake 20 minutes at 400 °F.") keeps the rest
     as its step, and the yield is "12 muffins" (review of the branch, CK16-R3: the whole step was
     dropped and the baking instruction taken as the yield).
   - Mixed numbers and wrapped lines (S23): a fraction sign after a whole number is one mixed number
     however it is set ("1¼", "1 ¼" with a no-break space, "1 1/4" are all 1¼; page 18 was dropped
     because "1¼" read as 11/4 against the model's 5/4), a fraction in raised figures ("¹/ 8") is
     closed up, and an entry of a list set over several lines of a narrow column is found joined
     ("1½ tablespoon" / "Hot 'n Spicy Seasoning" / "(see recipe on page 19)": page 9 was dropped; a
     line after one that ends with "into", "or", "and", a comma or a bracket joins it too).
   - **Long runs** (checkpoint 7, correction 2). Books run in the plan's order. `--max-calls N` caps
     the model calls of a run (0, the default, is no cap): past it nothing more is asked, every
     answer stays, the rest is counted `model call budget spent`, and the run stops with exit 3;
     the next run (the next day) asks only what is still missing, book by book, and a book whose
     calls are all answered is `done` in its status file.
   - **One model step at a time** (checkpoint 7, correction 1: NCI and the Library asked the shared
     host side by side and waited on each other's calls). Every `extract` run on one work folder
     takes its turn on a lock file (`/work/model-queue.lock` in the image, `--queue` to name another)
     before it asks the model. A run that waits leaves a marker beside the lock and holds a lock on
     it while it waits; the run holding the turn gives it up after `--turn` calls (default 20) or at
     the end of a document when a held marker is there, waits until the other run has taken it, and
     queues behind it for its next turn. So NCI and the Library queue behind each other instead of
     asking side by side. A marker no run holds was left by a run stopped while it waited (`docker
     stop` ends PID 1 with SIGKILL): it is no waiting run, and the next run that looks removes it and
     logs it (review of the branch, CK16-R2: the stale marker made every later run pause 120 s every
     20 calls).
   - Model calls (`book`, `pages`): `--timeout` (seconds per call, default 600) and `--retries`
     (default 2, waits doubling from 30 s). Every readable answer is appended to
     `records/<doc>.answers.jsonl` the moment it arrives, keyed by the model, the prompt and the
     text, and read back on the next run with the same `--model`, so a page or chunk is never asked
     twice; an unreadable answer is not kept, so it is asked again and is counted as a call left
     (`unreadable answer`), and the book is not `done` until it is answered (review of the branch,
     CK16-R1: the run counted it done while the plan counted it left); another model's answers are
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

   Nothing else is taken: no image (licences exclude some; a Library page image is read for its
   text only, then deleted, and never kept as a recipe's image), and none of the sources' nutrition
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

## Page images read by their layout (staging checkpoint 9, U17)

The one-book proof read 4 of 20 pages wrong: an index page's names took the other column's page numbers ("Waffles
26" where the book prints 16) and whole method lines were dropped without a mark. `table_text` now reads a page by
its layout (`page_layout`):

- **Two columns** (a gutter at least two letters wide down the middle that no word of the body crosses, at least
  three rows on each side, most right-hand rows starting with a letter): the heading rows above the first two-sided
  row are read first, then the left column, then the right one, each as a page of its own, so a name only ever meets
  the numbers of its own column.
- **A page whose rows cannot be assigned to one column** (a few body rows cross the gutter of a page set in two
  columns) is refused: the reading is `COLUMNS_REFUSED`, the page keeps the Library's text, is marked
  `imageRefused: columns` and stays `needsImage`, so no record is taken from it by rule or by the model.
- **A line the reading lost is marked:** a wide gap inside a paragraph (the line before goes on with no sentence end
  or ends with a hyphen, or the line after starts in lower case) gets a line of the unread mark, so the recipe is
  unreadable by rule and never kept with a step missing.

## Page images: the one-book proof on staging

Before any larger run, one Library book is taken through the page images on staging, with the
image built from the release commit (`ops/hand-import-box.sh build`):

1. In the image, the reading tests on the test pages with the image's own tesseract:
   `ops/hand-import-box.sh selftest` from a copy of the release commit's `ops` and `docs`
   (`git archive <tag> ops docs`): it runs `ops/tests/test_hand_import*.py` in the image with `ops/`
   and `docs/` mounted read-only (the determination test reads `docs/architecture/crosscheck-sources.md`;
   staging checkpoint 8, correction 2, found the bare `docker run` with `ops/` alone erroring on it). The
   real-tesseract tests must run (not skip) and pass; record the output.
2. Pick one book of `books.csv` already fetched and read at an earlier checkpoint (Lizzie's cook book,
   `loc-lizzie`, with its garbled tables, is the one S22 found), and give its row alone in a books file
   (`--books /work/loc/books-proof.csv`).
3. `text --source loc --books ...` and record `pagesNeedingImage`.
4. `images --source loc --books ... --limit 50`: read the report. Every refusal must name its limit;
   `requests` and the run's time must fit 6 s a request. Repeat until no `leftForNextRun`.
5. **Before the second `text` run** (it deletes the images), draw the pages to read against their
   images (a seeded draw, about 20 of the fetched pages), copy those images aside, read each page's
   image reading against its image (every amount, every name, index numbers), then delete the copies
   wherever they were made; record it as step 7's reading of the images (staging checkpoint 8,
   correction 3). Then `text --source loc --books ...` again: record `imagesDeleted`, check that no
   `*.p????.jpg` and no `*.p????.*.part` is left
   in `/work/loc/docs` and that `SHA256SUMS` passes `sha256sum -c`; record the pages that turned
   `loc-image-ocr`.
6. `plan` and `extract` for the book. A rule-only extract runs with no model host file
   (`NOM_HAND_MODEL_HOST_FILE` unset or the file removed: the wrapper says `extract: rules only`), so
   it never reaches the host by accident; for the model step copy the mode-600 file in, run, and
   remove it (staging checkpoint 8, correction 4). Record `keptByRule`, the unreadable recipes and the
   model calls.
7. Read every record kept from an image page against the page on the Library's site in a browser
   (not fetched by script): name, every amount, every food, the steps. The bar is the same as for any
   hand import (0 wrong records of the sample). Then sample, stage the dry run, apply, prove the
   undo, as before.
8. Stop rule: one wrong amount kept from an image is a failure; no larger run until the cause is
   found and fixed. A low yield (most recipes unreadable) is not a failure, but is reported with
   the rows that carried the mark.

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
(Debian) with `poppler-utils` (`pdftotext`, `pdftoppm`, GPL), `tesseract-ocr` with
`tesseract-ocr-eng` (Apache-2.0) and `wamerican` (SCOWL's word list, a permissive licence, for the
readability test) and Pillow (HPND, to crop and enlarge an amount cell of a page image for its second
reading); the script needs nothing else beyond Python's standard library. The image holds
the script, the two modules it loads (`cookbook-extract.py`, `recipe-source-attach.py`) and the two
lists, and nothing else: no secret, no host name, no address. One image serves staging and
production; only the environment of a run differs.

`ops/hand-import-box.sh` runs every step in it (`fetch`, `links`, `text`, `images`, `plan`, `extract`,
`sample`, `sources`, `stage`, `withhold`, `release`), `build` builds it, and `selftest` runs the reading
tests in it with the checkout's `ops/` and `docs/` mounted read-only; `plan` gets `--model` from
`NOM_HAND_MODEL` like `extract`. It reads at run time:

| Variable | Use |
|---|---|
| `NOM_HAND_WORK` | the work folder on the server, mounted at `/work` (required for every step with `--out`; `--out` defaults to `/work` and must lie under it) |
| `NOM_HAND_MODEL_HOST_FILE` | a file of the caller's, mode 600 (or 400), whose first line is the model host for `extract`, passed as `--host`; copied in only for a model step and removed after, so the address stays out of the environment and a rule-only `extract` (no file: `extract: rules only`) never reaches the host. `NOM_HAND_MODEL_HOST` is refused (staging checkpoint 8, correction 4) |
| `NOM_HAND_MODEL` | the model for `extract` and `plan`, passed as `--model` (default `qwen2.5:14b-instruct`) |
| `NOM_API`, `NOM_API_KEY`, `NOM_API_TOKEN` | the NOM API of the environment being imported into, passed into the container by name, never on the command line |
| `NOM_HAND_NETWORK` | a docker network for the container (`host` when `NOM_API` is a loopback address) |
| `NOM_HAND_IMAGE` | the image tag (default `nom-hand-import`) |
| `NOM_HAND_DRY_RUN=1` | print the `docker` command instead of running it |

The container runs as the calling user, read-only, with a private `/tmp`, no capabilities and no new
privileges, and is removed when the step ends. The import is run from the server, staging first;
production only in the one rollout, with the same image and the same steps.

For the `book` and `pages` layouts the model host serves `qwen2.5:14b-instruct` (Apache-2.0).

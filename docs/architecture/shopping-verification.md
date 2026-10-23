# Shopping list, shared check-off and household label verification

Release A of the shopping lane (v0.3.93). The live Shopping page keeps deriving its lines
from the meal plan and the pantry; this release adds three things beside those lines:
items a household member types in by hand, check-off state shared on the server, and a
way for a member to tell us what a package says when the catalog has no verified
nutrition for the food they just ticked.

Everything here is **evidence, not catalog data**. Nothing a household supplies is written
to `nutrient.IngredientNutrient` or to `curation.FoodCatalogProposal` in this release.

## Tables

| Table | What a row is |
|---|---|
| `shopping.HouseholdShoppingItem` | One ad-hoc line on a household's list. `IngredientId` links it to the catalog food it matched or created; `Checked`/`CheckedDate`/`CheckedByPersonId` carry its own check state. Soft-deleted when removed. |
| `shopping.HouseholdShoppingCheck` | The check mark on a *computed* line (plan or pantry). One row per `(HouseholdId, ItemKey)`, unique; `ItemKey` is the UI's key for the line, today the ingredient id. Rows are toggled in place (`Checked`), never deleted and re-inserted, because `SaveChanges` turns a delete into a soft delete and the unique index would then block the next check. A check counts only while `CheckedDate` is within `Shopping:CheckTtlDays` (default 3); older rows are reported unchecked and flipped lazily on the next read. This replaces the per-day `localStorage` reset. |
| `recipe.IngredientVerification` | One user action about a food lacking verified nutrition: a label photo plus the values read off it (`Kind=label`), a barcode (`barcode`), package measurements (`measurements`), or a skip (`skip`). `HouseholdId` scopes it; NULL is reserved for lane-produced global rows in Release B. `LastAskedDate` drives the 30-day quiet window. |

## "Lacks verified nutrition"

A food is verified when it is not deleted, has `FdcId` set **or** `CurationStatusId = Curated`,
**and** has an `IngredientNutrient` row for the nutrient named `Calories`. A user-authored
pending food with hand-typed nutrient rows is not verified. The Calories nutrient is looked
up by name and its absence throws; it is never defaulted to 0.

The household is asked about a food when it lacks verified nutrition and no verification row
for `(ingredient, household)` has `LastAskedDate` within 30 days or an open status
(`pending`, `corroborated`, `published`). A `skipped` row therefore silences the question
for 30 days; a `pending` label scan silences it until the lane resolves it.

## Source namespaces

`IngredientVerification.Source` records where evidence came from:

| Prefix | Meaning | Who writes it |
|---|---|---|
| `label:user:{personId}` | A household member's own reading of a package label (Release A, every row) | `IngredientVerificationService` |
| `fdc:{id}` | USDA FoodData Central branded record | Release B lane |
| `manufacturer:{host}` | The manufacturer's own product page, on a host with a determination in `crosscheck-sources.md` | Release B lane |
| `off:{barcode}` | Open Food Facts, **signal only** (ODbL) | Release B lane |

`ProposalPolicy` treats any `label:` prefix as authoritative because the prefix was defined
to mean *a corroborated published label*. `label:user:` is explicitly carved out
(`ProposalPolicy.HouseholdLabelPrefix`), so even if a verification's `Source` were copied
into a proposal it could not change a nutrient value. Release B must mint `label:`
proposals only after corroboration (two independent sources within tolerance), never by
copying `IngredientVerification.Source`.

## What is stored from a label scan

The label endpoint stores the photo (re-encoded JPEG, EXIF stripped, max width 2000 px; in
`LabelImageData` or under `Media:RootPath` as `ingredient-labels/{ingredientId}/{guid}.jpg`),
the raw Tesseract text (`OcrText`, capped at 16 000 chars) and `NutrientsJson`:

```json
{
  "basis": "serving",
  "servingGrams": 55,
  "servingText": "2/3 cup",
  "calories": 230,
  "values": [ { "key": "fat", "amount": 8, "unit": "g", "percentDailyValue": 10 } ],
  "notSignificantSource": ["trans_fat"],
  "parserVersion": 1
}
```

Values are **per serving as printed**. Per-100 g numbers are never stored; they are derived
on read from `ServingGrams` alone. A serving printed only in millilitres yields no per-100 g
view, because 1 g/mL is a guess. A %DV never becomes an amount. The parser
(`NutritionFactsParser`) is pure and deterministic; no model is involved anywhere in this lane.

## Endpoints

All under `/api/Shopping`, `[Authorize]`, household-scoped through the caller's
`HouseholdMember` claims. Endpoints that create or list take `householdId`; endpoints that
address an existing row load it first and check *its* household, ignoring the body.

- `GET ?householdId=` state (items + live checks)
- `POST items`, `GET|PUT|DELETE items/{id}`
- `POST checks` (`itemKey` xor `itemId`), `POST checks/clear`
- `GET needs-verification?householdId=&ingredientIds=`
- `POST verification/{ingredientId}/skip|label|barcode|measurements`
- `PUT verification/{verificationId}` (confirm/correct a label scan)
- `GET verification?householdId=&ingredientId=`, `GET verification/{verificationId}/image`

Label scans are limited to 20 per person per hour on top of the global rate limiter.

## Release B extension point

`IVerificationLookupLane` (`Nom.Orch.Interfaces`) is called with the verification id after
every label, barcode or measurements write. Release A registers
`NoOpVerificationLookupLane` (`IsConfigured => false`, returns a completed task) explicitly
in `Program.cs`, because the DI scanner only auto-wires names ending in `Service`. No
`HttpClient` exists for it and no third-party host is contacted. Release B replaces the
registration with a hosted-service-backed lane gated by configuration and quiet hours.

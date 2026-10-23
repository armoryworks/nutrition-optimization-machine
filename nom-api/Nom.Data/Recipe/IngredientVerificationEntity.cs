using System;
using Nom.Data.Plan;

namespace Nom.Data.Recipe
{
    /// <summary>
    /// What a household told us about a food whose nutrition is not verified: a label photo
    /// with the values read off it, a barcode, package measurements, or that they skipped the
    /// question. Rows are evidence, not catalog data — nothing here is ever copied into
    /// <c>nutrient.IngredientNutrient</c> or into a curation proposal without corroboration,
    /// and <see cref="Source"/> is deliberately not a <c>FoodCatalogProposal.Source</c>.
    /// </summary>
    public class IngredientVerificationEntity : BaseEntity
    {
        public long IngredientId { get; set; }
        public virtual IngredientEntity? Ingredient { get; set; }

        /// <summary>The household that supplied the evidence; NULL is reserved for lane-produced global rows.</summary>
        public long? HouseholdId { get; set; }
        public virtual HouseholdEntity? Household { get; set; }

        /// <summary>Where the evidence came from, e.g. <c>label:user:{personId}</c>.</summary>
        public string Source { get; set; } = string.Empty;

        /// <summary>pending | corroborated | published | held | skipped — see <see cref="IngredientVerificationStatus"/>.</summary>
        public string Status { get; set; } = IngredientVerificationStatus.Pending;

        /// <summary>label | barcode | measurements | skip — see <see cref="IngredientVerificationKind"/>.</summary>
        public string Kind { get; set; } = IngredientVerificationKind.Skip;

        public string? LabelImagePath { get; set; }
        public byte[]? LabelImageData { get; set; }
        public string? LabelImageContentType { get; set; }

        /// <summary>Raw OCR output kept for re-parsing and audit.</summary>
        public string? OcrText { get; set; }

        /// <summary>GTIN-8/12/13/14, digits only.</summary>
        public string? Upc { get; set; }

        public string? ServingText { get; set; }
        public decimal? ServingGrams { get; set; }
        public decimal? ServingMl { get; set; }
        public decimal? ServingsPerContainer { get; set; }
        public decimal? PackageGrams { get; set; }
        public decimal? PackageMl { get; set; }
        public decimal? PiecesPerPackage { get; set; }
        public decimal? CupGrams { get; set; }

        /// <summary>Per-serving values as parsed or as the user corrected them; stored only.</summary>
        public string? NutrientsJson { get; set; }

        public bool Confirmed { get; set; }
        public long? ConfirmedByPersonId { get; set; }
        public DateTime? ConfirmedAt { get; set; }

        /// <summary>When the household was last asked about this food; drives the 30-day quiet window.</summary>
        public DateTime LastAskedDate { get; set; }

        public long? ReviewedByPersonId { get; set; }
        public DateTime? ReviewedAt { get; set; }
    }

    public static class IngredientVerificationStatus
    {
        public const string Pending = "pending";
        public const string Corroborated = "corroborated";
        public const string Published = "published";
        public const string Held = "held";
        public const string Skipped = "skipped";
    }

    public static class IngredientVerificationKind
    {
        public const string Label = "label";
        public const string Barcode = "barcode";
        public const string Measurements = "measurements";
        public const string Skip = "skip";
    }
}

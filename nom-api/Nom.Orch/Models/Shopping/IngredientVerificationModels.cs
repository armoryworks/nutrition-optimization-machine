using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Nom.Orch.Models.Shopping
{
    public class NeedsVerificationModel
    {
        public long IngredientId { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool HasVerifiedNutrition { get; set; }
        public bool HasUpc { get; set; }
        public bool AskedRecently { get; set; }
        public bool ShouldAsk { get; set; }
    }

    public class LabelScanResultModel
    {
        public long VerificationId { get; set; }
        public NutritionFactsParseResult Parsed { get; set; } = new();
        public string OcrText { get; set; } = string.Empty;
        public bool ImageStored { get; set; }
    }

    public class ConfirmNutrientModel
    {
        [Required]
        [StringLength(64)]
        public string Key { get; set; } = string.Empty;

        [Range(0, 9_999_999)]
        public decimal? Amount { get; set; }

        [StringLength(8)]
        public string Unit { get; set; } = "g";

        [Range(0, 10_000)]
        public int? PercentDailyValue { get; set; }
    }

    /// <summary>Basis defaults to what the scan was parsed as; per100g values are stored as printed and never scaled by a serving weight.</summary>
    public class IngredientVerificationConfirmModel
    {
        [RegularExpression("^(serving|per100g)$")]
        public string? Basis { get; set; }

        [StringLength(255)]
        public string? ServingText { get; set; }

        [Range(0, 9_999_999)]
        public decimal? ServingGrams { get; set; }

        [Range(0, 9_999_999)]
        public decimal? ServingMl { get; set; }

        [Range(0, 9_999_999)]
        public decimal? ServingsPerContainer { get; set; }

        [Range(0, 9_999_999)]
        public decimal? Calories { get; set; }

        [MaxLength(64)]
        public List<ConfirmNutrientModel> Nutrients { get; set; } = new();
    }

    /// <summary>IngredientRetired is true when the household's own unreviewed catalog entry was soft-deleted along with the unlinking.</summary>
    public class NotFoodResultModel
    {
        public long IngredientId { get; set; }
        public int ItemsUnlinked { get; set; }
        public bool IngredientRetired { get; set; }
    }

    public class IngredientVerificationModel
    {
        public long Id { get; set; }
        public long IngredientId { get; set; }
        public long? HouseholdId { get; set; }
        public string Kind { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public string? Upc { get; set; }
        public string? ServingText { get; set; }
        public decimal? ServingGrams { get; set; }
        public decimal? ServingMl { get; set; }
        public decimal? ServingsPerContainer { get; set; }
        public decimal? PackageGrams { get; set; }
        public decimal? PackageMl { get; set; }
        public decimal? PiecesPerPackage { get; set; }
        public decimal? CupGrams { get; set; }
        public NutritionFactsParseResult? Nutrients { get; set; }
        public bool Confirmed { get; set; }
        public List<string> Warnings { get; set; } = new();
        public DateTime LastAskedDate { get; set; }
        public DateTime CreatedDate { get; set; }
    }

    public class BarcodeRequestModel
    {
        [Required]
        public long HouseholdId { get; set; }

        [Required]
        [StringLength(64, MinimumLength = 8)]
        public string Upc { get; set; } = string.Empty;
    }

    public static class BarcodeSource
    {
        public const string Client = "client";
        public const string Server = "server";
    }

    /// <summary>VerificationId is null when nothing could be decoded from the image, because nothing was recorded.</summary>
    public class BarcodeResultModel
    {
        public long? VerificationId { get; set; }
        public string? Upc { get; set; }
        public bool Decoded { get; set; }
        public string Source { get; set; } = BarcodeSource.Client;
        public string? Format { get; set; }
        public bool AppliedToIngredient { get; set; }
    }

    public class IngredientMeasurementsModel
    {
        [Required]
        public long HouseholdId { get; set; }

        [Range(0.0001, 1_000_000)]
        public decimal? PackageAmount { get; set; }

        [RegularExpression("^(g|kg|oz|lb|ml|l|floz)$")]
        public string? PackageUnit { get; set; }

        [Range(0.0001, 1_000_000)]
        public decimal? PiecesPerPackage { get; set; }

        [Range(0.0001, 1_000_000)]
        public decimal? CupGrams { get; set; }
    }
}

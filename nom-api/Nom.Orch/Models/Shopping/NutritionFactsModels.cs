using System.Collections.Generic;

namespace Nom.Orch.Models.Shopping
{
    /// <summary>One line of a Nutrition Facts panel as printed: the amount per serving, never per 100 g.</summary>
    public class NutrientReading
    {
        public string Key { get; set; } = string.Empty;
        public decimal? Amount { get; set; }
        public string Unit { get; set; } = "g";
        public decimal? LessThan { get; set; }
        public int? PercentDailyValue { get; set; }
        public string Raw { get; set; } = string.Empty;
    }

    public class Per100gReading
    {
        public string Key { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string Unit { get; set; } = "g";
    }

    /// <summary>Per-100 g values, present only when an explicit gram weight made them derivable.</summary>
    public class Per100gNutrition
    {
        public decimal? Calories { get; set; }
        public List<Per100gReading> Nutrients { get; set; } = new();
    }

    public static class NutritionBasis
    {
        public const string Serving = "serving";
        public const string Per100g = "per100g";
        public const string Unknown = "unknown";
    }

    public class NutritionFactsParseResult
    {
        public bool Parsed { get; set; }
        public string Basis { get; set; } = NutritionBasis.Unknown;
        public string? ServingText { get; set; }
        public decimal? ServingGrams { get; set; }
        public decimal? ServingMl { get; set; }
        public decimal? ServingsPerContainer { get; set; }
        public bool ServingsPerContainerApprox { get; set; }
        public bool DualColumn { get; set; }
        public string? SecondColumnLabel { get; set; }
        public decimal? Calories { get; set; }
        public List<NutrientReading> Nutrients { get; set; } = new();
        public List<string> NotSignificantSource { get; set; } = new();
        public Per100gNutrition? Per100g { get; set; }
        public List<string> Warnings { get; set; } = new();
        public int FieldsRecognised { get; set; }
        public decimal Confidence { get; set; }
        public List<string> MatchedLines { get; set; } = new();
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Nom.Orch.Models.Shopping;

namespace Nom.Orch.Services.Support
{
    /// <summary>
    /// The shape kept in <c>IngredientVerification.NutrientsJson</c>: per-serving amounts as read
    /// or as confirmed, plus the serving they belong to. Per-100 g values are never stored; they
    /// are re-derived on read from the explicit gram weight so the two can never disagree.
    /// </summary>
    public sealed class StoredNutrients
    {
        public string Basis { get; set; } = NutritionBasis.Unknown;
        public decimal? ServingGrams { get; set; }
        public string? ServingText { get; set; }
        public decimal? Calories { get; set; }
        public List<StoredNutrientValue> Values { get; set; } = new();
        public List<string> NotSignificantSource { get; set; } = new();
        public int ParserVersion { get; set; } = NutritionFactsParser.ParserVersion;
    }

    public sealed class StoredNutrientValue
    {
        public string Key { get; set; } = string.Empty;
        public decimal? Amount { get; set; }
        public string Unit { get; set; } = "g";
        public decimal? LessThan { get; set; }
        public int? PercentDailyValue { get; set; }
    }

    public static class VerificationNutrients
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        public static string ToJson(NutritionFactsParseResult parsed)
        {
            var stored = new StoredNutrients
            {
                Basis = parsed.Basis,
                ServingGrams = parsed.ServingGrams,
                ServingText = parsed.ServingText,
                Calories = parsed.Calories,
                Values = parsed.Nutrients.Select(n => new StoredNutrientValue
                {
                    Key = n.Key,
                    Amount = n.Amount,
                    Unit = n.Unit,
                    LessThan = n.LessThan,
                    PercentDailyValue = n.PercentDailyValue,
                }).ToList(),
                NotSignificantSource = parsed.NotSignificantSource.ToList(),
            };
            return JsonSerializer.Serialize(stored, Options);
        }

        public static NutritionFactsParseResult? FromJson(string? json, decimal? servingGrams, string? servingText)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            StoredNutrients? stored;
            try
            {
                stored = JsonSerializer.Deserialize<StoredNutrients>(json, Options);
            }
            catch (JsonException)
            {
                return null;
            }
            if (stored == null) return null;

            var result = new NutritionFactsParseResult
            {
                Parsed = stored.Calories != null || stored.Values.Count > 0,
                Basis = stored.Basis,
                ServingGrams = servingGrams ?? stored.ServingGrams,
                ServingText = servingText ?? stored.ServingText,
                Calories = stored.Calories,
                Nutrients = stored.Values.Select(v => new NutrientReading
                {
                    Key = v.Key,
                    Amount = v.Amount,
                    Unit = v.Unit,
                    LessThan = v.LessThan,
                    PercentDailyValue = v.PercentDailyValue,
                }).ToList(),
                NotSignificantSource = stored.NotSignificantSource.ToList(),
            };
            result.FieldsRecognised = result.Nutrients.Count + (result.Calories != null ? 1 : 0);
            return result;
        }
    }
}

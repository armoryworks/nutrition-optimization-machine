using System;
using System.Collections.Generic;
using System.Linq;

namespace Nom.Data.Nutrition
{
    /// <summary>
    /// What a proposal is allowed to change, based on where it came from.
    ///
    /// The governing rule: <b>an automated reviewer may propose names, food groups, flags and
    /// coverage gaps — never a nutrient value.</b> Language models have no authoritative nutrition
    /// data; a model-authored number would replace a measured value with a plausible guess, which
    /// is strictly worse than leaving the record alone. Numeric changes must cite an authoritative
    /// source (an FDC record, or a corroborated published label), and even then land as proposals
    /// for a human to approve.
    /// </summary>
    public static class ProposalPolicy
    {
        /// <summary>Fields that carry measured nutrition and are therefore source-restricted.</summary>
        public static readonly string[] NutrientFields =
        {
            "calories", "kcal", "energy", "protein", "carb", "carbohydrate", "fat",
            "reference_serving_grams", "serving", "amount",
        };

        /// <summary>Source prefixes considered authoritative enough to propose a numeric change.</summary>
        public static readonly string[] AuthoritativeSourcePrefixes =
        {
            "fdc:",            // a USDA FoodData Central record
            "label:",          // a corroborated published nutrition label (>= 2 agreeing sources)
            "admin:",          // a human admin entering a value deliberately
            "deterministic:",  // computed by our own audit (e.g. unit normalization)
        };

        public static bool IsNutrientField(string? field) =>
            !string.IsNullOrWhiteSpace(field)
            && NutrientFields.Any(f => field.Contains(f, StringComparison.OrdinalIgnoreCase));

        public static bool IsAuthoritativeSource(string? source) =>
            !string.IsNullOrWhiteSpace(source)
            && AuthoritativeSourcePrefixes.Any(p => source.StartsWith(p, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Whether a proposal may be accepted at ingest. Returns false with a reason when a
        /// non-authoritative source (e.g. a model reviewer) tries to change a nutrient value.
        /// </summary>
        public static bool IsAllowed(string? field, string? source, out string? rejection)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                rejection = "source_required";
                return false;
            }

            if (IsNutrientField(field) && !IsAuthoritativeSource(source))
            {
                rejection = "nutrient_change_requires_authoritative_source";
                return false;
            }

            rejection = null;
            return true;
        }
    }
}

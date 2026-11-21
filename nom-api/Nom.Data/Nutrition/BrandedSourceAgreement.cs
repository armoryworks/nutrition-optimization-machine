using System;
using System.Collections.Generic;
using System.Linq;

namespace Nom.Data.Nutrition
{
    /// <summary>
    /// One value as a source states it per 100 g: an amount, or "less than" an upper bound (a label's
    /// "&lt;1 g").
    /// </summary>
    public sealed record SourceValue(decimal? Amount, decimal? LessThan = null)
    {
        public static SourceValue? Of(decimal? amount) => amount is { } a ? new SourceValue(a) : null;
    }

    /// <summary>
    /// What one source says about a branded food, per 100 g: energy, protein, carbohydrate and fat.
    /// <see cref="Key"/> names the source once (one household, one manufacturer host, Open Food
    /// Facts), so two readings of one source count once. <see cref="ServingGrams"/> is the serving
    /// its figures were printed for, which sets how much label rounding is allowed; null when they
    /// were printed per 100 g.
    /// </summary>
    public sealed record BrandedSourceReading(
        string Key,
        string Kind,
        decimal? ServingGrams,
        SourceValue? Kcal,
        SourceValue? Protein,
        SourceValue? Carb,
        SourceValue? Fat);

    /// <summary>The stored per-100 g values of a branded food (its FDC branded record) and the serving USDA lists for it.</summary>
    public sealed record BrandedStoredValues(decimal? Kcal, decimal? Protein, decimal? Carb, decimal? Fat, decimal? ServingGrams);

    /// <summary>What one source's reading came to: its key, kind, verdict and, when it disagrees, the first value that does.</summary>
    public sealed record BrandedSourceVerdict(string Key, string Kind, string Verdict, string? Detail = null);

    /// <summary>
    /// The agreement of every source with a branded food's stored values: how many agree, counting
    /// the FDC branded record itself as one, how many disagree, and each source's verdict.
    /// </summary>
    public sealed record BrandedAgreement(int Agreeing, int Disagreeing, IReadOnlyList<BrandedSourceVerdict> Verdicts)
    {
        public IEnumerable<string> AgreeingKeys => Verdicts.Where(v => v.Verdict == BrandedSourceAgreement.Agrees).Select(v => v.Key);
    }

    /// <summary>
    /// Compares the sources a branded food has with its stored FDC branded values, with no model and
    /// without ever changing a value. A value agrees when it lies within 10 % of the stored value or
    /// within the two figures' label rounding, whichever is wider: half the FDA rounding step (5 kcal,
    /// 0.5 g) scaled to 100 g by each figure's printed serving, or half a per-100 g step (0.5 kcal,
    /// 0.05 g) for a figure printed per 100 g. A "less than" value agrees when the stored value is no
    /// more than its bound plus that allowance. A source agrees only when energy, protein,
    /// carbohydrate and fat all agree; one value outside disagrees; a missing value leaves the
    /// source incomplete, counted neither way. A household label (one per household) and a
    /// manufacturer's page (one per host) are independent sources. A later FDC branded release is the
    /// same submitter and never counts. Open Food Facts is a signal only: a disagreement counts
    /// against the food, an agreement never for it. Anything else is ignored.
    /// </summary>
    public static class BrandedSourceAgreement
    {
        public const string Version = "branded-agreement/1";

        public const string HouseholdLabel = "household-label";
        public const string Manufacturer = "manufacturer";
        public const string FdcRelease = "fdc-release";
        public const string OpenFoodFacts = "open-food-facts";

        public const string Agrees = "agrees";
        public const string Disagrees = "disagrees";
        public const string Incomplete = "incomplete";
        public const string NotIndependent = "not-independent";
        public const string SignalAgrees = "signal-agrees";
        public const string SignalDisagrees = "signal-disagrees";
        public const string Ignored = "ignored";

        public const string StoredRecordKey = "fdc-record";

        public const decimal RelativeTolerance = 0.10m;
        public const decimal KcalHalfStep = 5m;
        public const decimal GramHalfStep = 0.5m;
        public const decimal KcalHalfStepPer100g = 0.5m;
        public const decimal GramHalfStepPer100g = 0.05m;

        /// <summary>Whether a source of this kind counts toward agreement when it agrees.</summary>
        public static bool IsIndependent(string kind) => kind is HouseholdLabel or Manufacturer;

        /// <summary>
        /// Judges every reading against the stored values. A source given more than once is judged
        /// by its last reading. When the stored values lack energy or a macro, nothing agrees.
        /// </summary>
        public static BrandedAgreement Judge(BrandedStoredValues stored, IEnumerable<BrandedSourceReading> readings)
        {
            var complete = stored.Kcal != null && stored.Protein != null && stored.Carb != null && stored.Fat != null;
            var verdicts = new List<BrandedSourceVerdict>();
            var agreeing = complete ? 1 : 0;
            var disagreeing = 0;
            var last = new Dictionary<string, BrandedSourceReading>(StringComparer.Ordinal);
            var order = new List<string>();
            foreach (var reading in readings)
            {
                if (!last.ContainsKey(reading.Key)) order.Add(reading.Key);
                last[reading.Key] = reading;
            }

            foreach (var key in order)
            {
                var reading = last[key];
                if (reading.Kind == FdcRelease)
                {
                    verdicts.Add(new BrandedSourceVerdict(key, reading.Kind, NotIndependent));
                    continue;
                }
                if (!IsIndependent(reading.Kind) && reading.Kind != OpenFoodFacts)
                {
                    verdicts.Add(new BrandedSourceVerdict(key, reading.Kind, Ignored));
                    continue;
                }

                var (verdict, detail) = complete ? Compare(stored, reading) : (Incomplete, "stored values incomplete");
                if (reading.Kind == OpenFoodFacts)
                {
                    if (verdict == Agrees) verdict = SignalAgrees;
                    else if (verdict == Disagrees)
                    {
                        verdict = SignalDisagrees;
                        disagreeing++;
                    }
                }
                else if (verdict == Agrees) agreeing++;
                else if (verdict == Disagrees) disagreeing++;
                verdicts.Add(new BrandedSourceVerdict(key, reading.Kind, verdict, detail));
            }
            return new BrandedAgreement(agreeing, disagreeing, verdicts);
        }

        private static (string Verdict, string? Detail) Compare(BrandedStoredValues stored, BrandedSourceReading reading)
        {
            var missing = new List<string>();
            foreach (var (name, storedValue, value, halfStep, halfStepPer100g) in new[]
            {
                ("kcal", stored.Kcal!.Value, reading.Kcal, KcalHalfStep, KcalHalfStepPer100g),
                ("protein", stored.Protein!.Value, reading.Protein, GramHalfStep, GramHalfStepPer100g),
                ("carb", stored.Carb!.Value, reading.Carb, GramHalfStep, GramHalfStepPer100g),
                ("fat", stored.Fat!.Value, reading.Fat, GramHalfStep, GramHalfStepPer100g),
            })
            {
                if (value == null || (value.Amount == null && value.LessThan == null))
                {
                    missing.Add(name);
                    continue;
                }
                var allowance = Math.Max(RelativeTolerance * Math.Abs(storedValue),
                    Rounding(stored.ServingGrams, halfStep, halfStepPer100g) + Rounding(reading.ServingGrams, halfStep, halfStepPer100g));
                var agrees = value.Amount is { } amount
                    ? Math.Abs(amount - storedValue) <= allowance
                    : storedValue <= value.LessThan!.Value + allowance;
                if (!agrees)
                    return (Disagrees, value.Amount is { } a
                        ? $"{name} {Format(a)} against {Format(storedValue)}"
                        : $"{name} under {Format(value.LessThan!.Value)} against {Format(storedValue)}");
            }
            return missing.Count == 0 ? (Agrees, null) : (Incomplete, "no " + string.Join(", ", missing));
        }

        private static decimal Rounding(decimal? servingGrams, decimal halfStep, decimal halfStepPer100g) =>
            servingGrams is { } g && g > 0m ? halfStep * 100m / g : halfStepPer100g;

        private static string Format(decimal value) => Math.Round(value, 2).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Nom.Orch.Services.Support
{
    /// <summary>One line of an unclear alcohol wording as the fixed-reading readers read it: the raw line, its recipe's title, a few of its other lines and the start of its steps.</summary>
    public sealed record FixedLine(string Raw, string? Title, IReadOnlyList<string> OtherLines, string? Steps, bool HasSourceSite);

    /// <summary>The lines of one wording a fixed reading is asked on, and which of the three unclear words it is.</summary>
    public sealed record FixedPack(string Wording, string Mode, IReadOnlyList<FixedLine> Lines);

    /// <summary>
    /// The fixed-reading mode of the readers for the alcohol words the rules leave unclear (alcohol
    /// design 2.3, plan 2.3 A6): bare "wine" in an old text, bare "cider" and "alcohol" with no food
    /// context. A reader does not choose a food: it chooses one of a few readings for each line
    /// (wine: table or fortified; cider: sweet or hard; alcohol: drunk or not food; or unsure), and a
    /// reading maps to a fixed USDA food and to the alcohol sense the link gate then reads. Both
    /// readers must give the same reading on every line read, no data rule may contradict it on any
    /// line, and an "unsure" from either writes nothing. A model never supplies a number.
    /// </summary>
    public static class AlcoholFixedReading
    {
        public const string Version = "alcohol-fixed-reading/1";

        public const string Wine = "wine";
        public const string Cider = "cider";
        public const string Alcohol = "alcohol";
        public const string Unsure = "unsure";
        public const string NotFood = "not-food";

        public const int MaxLines = 12;
        public const int SmallLines = 3;
        public const int OtherLines = 8;
        public const int StepCharacters = 600;

        /// <summary>A reading of one mode: the alcohol sense it stands for, the USDA food it maps to, and the words that food's description uses for the wording's word when it lacks the word itself.</summary>
        public sealed record Reading(string Mode, string Code, string? Sense, string? FdcId, string? Means, string Gloss);

        private static readonly Reading[] Readings =
        {
            new(Wine, "table", "wine-table", "173185", null, "table wine (red, white or rose, drunk with a meal)"),
            new(Wine, "fortified", "fortified", "175112", null, "fortified wine (sherry, Madeira, port, sack)"),
            new(Cider, "sweet", "apple-juice", "173933", "apple juice", "sweet cider, unfermented apple juice"),
            new(Cider, "hard", "hard-cider", "174146", null, "hard cider, fermented and alcoholic"),
            new(Alcohol, "drunk", "spirit", "174815", "distilled", "a drinkable distilled spirit"),
            new(Alcohol, NotFood, null, null, null, "not food (lamp spirit, a cleaning or medicinal alcohol)"),
        };

        private static readonly Dictionary<string, string> Modes = new(StringComparer.Ordinal)
        {
            ["wine-unclear"] = Wine,
            ["cider-unclear"] = Cider,
            ["alcohol-unclear"] = Alcohol,
        };

        private static readonly Regex Fermented = new(@"\b(?:ferment\w*|barrels?|casks?|work(?:ed|ing)? (?:off|out)|yeast)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        /// <summary>The fixed-reading mode of a wording whose lines share an unclear alcohol sense, or null.</summary>
        public static string? ModeOf(AlcoholReading? shared) => shared != null && Modes.TryGetValue(shared.Sense, out var mode) ? mode : null;

        /// <summary>The readings a reader may give in a mode, "unsure" last.</summary>
        public static IReadOnlyList<string> Options(string mode) => Readings.Where(r => r.Mode == mode).Select(r => r.Code).Append(Unsure).ToList();

        public static IReadOnlyList<Reading> Of(string mode) => Readings.Where(r => r.Mode == mode).ToList();

        /// <summary>The reading of a mode by its code, or null for "unsure" or an unknown code.</summary>
        public static Reading? Find(string mode, string? code) => Readings.FirstOrDefault(r => r.Mode == mode && string.Equals(r.Code, code, StringComparison.Ordinal));

        /// <summary>
        /// Why a data rule refuses a reading for one line, or null: a fortified reading of bare wine
        /// in a recipe from a web source (a modern recipe means table wine), and a sweet reading of a
        /// cider the recipe ferments, barrels or works with yeast.
        /// </summary>
        public static string? Contradicts(Reading reading, FixedLine line)
        {
            if (reading.Mode == Wine && reading.Code == "fortified" && line.HasSourceSite) return "a fortified reading of bare wine in a modern recipe";
            if (reading.Mode == Cider && reading.Code == "sweet" && Fermented.IsMatch(string.Join(" \n ", new[] { line.Raw, line.Steps }.Where(s => !string.IsNullOrWhiteSpace(s)))))
                return "a sweet reading of a cider the recipe ferments";
            return null;
        }

        /// <summary>
        /// What two readers' readings of the same lines come to: the one reading both gave on every
        /// line, when no line is unsure or missing and no data rule contradicts it on any line;
        /// otherwise null with the reason.
        /// </summary>
        public static (Reading? Reading, string Reason) Combine(FixedPack pack, IReadOnlyList<string?> a, IReadOnlyList<string?> b)
        {
            if (pack.Lines.Count == 0) return (null, "no line to read");
            if (a.Count != pack.Lines.Count || b.Count != pack.Lines.Count || a.Any(r => r == null) || b.Any(r => r == null)) return (null, WordingReaders.Unreadable);
            if (a.Concat(b).Any(r => r == Unsure)) return (null, "a reader is unsure");
            var codes = a.Concat(b).Distinct(StringComparer.Ordinal).ToList();
            if (codes.Count != 1) return (null, $"{WordingReaders.Disagree}: {string.Join(", ", codes)}");
            var reading = Find(pack.Mode, codes[0]);
            if (reading == null) return (null, $"not a reading of {pack.Mode}: {codes[0]}");
            foreach (var line in pack.Lines)
                if (Contradicts(reading, line) is { } why) return (null, why + $": \"{line.Raw}\"");
            return (reading, $"both read {reading.Code} on every line");
        }

        internal static string SignatureSource()
        {
            var text = new StringBuilder(Version);
            foreach (var r in Readings) text.Append('\n').Append(string.Join('|', r.Mode, r.Code, r.Sense, r.FdcId, r.Means, r.Gloss));
            foreach (var (sense, mode) in Modes) text.Append("\nmode:").Append(sense).Append('=').Append(mode);
            text.Append("\nfermented:").Append(Fermented);
            text.Append("\nnumbers:").Append(MaxLines).Append(',').Append(SmallLines).Append(',').Append(OtherLines).Append(',').Append(StepCharacters);
            return text.ToString();
        }
    }
}

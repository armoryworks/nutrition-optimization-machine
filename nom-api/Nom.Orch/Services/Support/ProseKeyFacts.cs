using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Nom.Orch.Services.Support
{
    /// <summary>
    /// The key facts of a recipe whose own prose is withheld, built by rule from the source method:
    /// each time, temperature and pan size the method states (<see cref="ProseQuantities"/>), in step
    /// order, with the cooking verb of its sentence (<see cref="ProseVerbLexicon"/>: the word itself
    /// when it is a plain form, otherwise its group's name) and, for a temperature in an oven
    /// sentence, "oven"; a size is followed by the vessel it
    /// names. Numbers and units are written in one fixed form ("bake 30 to 40 min", "oven 350°F",
    /// "9 x 13-inch pan"), so no wording of the source is carried over: these are facts, not
    /// expression. Amounts of ingredients, counts and bracketed conversions are left out (the
    /// ingredient list states them), and so is how long the dish keeps (a time in a storing sentence); a
    /// temperature never takes the verb of work on a pan ("line", "grease"). Text only: no database and no model.
    /// </summary>
    public static class ProseKeyFacts
    {
        public const int MaximumFacts = 12;

        private static readonly Regex WordPattern = new(@"\p{L}+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex SentenceEnd = new(@"[.!?;]", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex DimensionJoin = new(@"^\s*(?:x|×|by)\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly HashSet<string> Vessels = new(StringComparer.OrdinalIgnoreCase)
        {
            "pan", "dish", "skillet", "tin", "pot", "sheet", "mold", "mould", "ramekin", "ramekins", "casserole", "tray", "springform", "loaf",
        };

        private static readonly string[] GeneralGroups = { "cook" };

        private static readonly Regex Inflected = new(@"(?:ing|ed|[^s]s)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly (decimal Value, string Text)[] Fractions =
        {
            (0.125m, "1/8"), (0.25m, "1/4"), (1m / 3m, "1/3"), (0.375m, "3/8"), (0.5m, "1/2"), (0.625m, "5/8"), (2m / 3m, "2/3"), (0.75m, "3/4"), (0.875m, "7/8"),
        };

        private sealed record Word(string Text, int Start);

        /// <summary>The key facts of <paramref name="source"/>'s method, at most <see cref="MaximumFacts"/>, without repeats.</summary>
        public static List<string> Build(RewriteFidelity.SourceText? source, ProseVerbLexicon? verbs = null)
        {
            var facts = new List<string>();
            if (source == null) return facts;
            verbs ??= ProseVerbLexicon.Default;
            foreach (var step in source.Steps)
            {
                foreach (var fact in FactsOf(ProseQuantities.Prepare(step), verbs))
                {
                    if (facts.Count >= MaximumFacts) return facts;
                    if (!facts.Contains(fact, StringComparer.Ordinal)) facts.Add(fact);
                }
            }

            return facts;
        }

        /// <summary>The key facts of the method stored in a schema.org Recipe node; empty when it states none or cannot be read.</summary>
        public static List<string> FromJsonLd(string? rawJsonLd) => Build(RewriteFidelity.Source(rawJsonLd));

        private static IEnumerable<string> FactsOf(string prepared, ProseVerbLexicon verbs)
        {
            var quantities = ProseQuantities.ReadPrepared(prepared)
                .Where(q => !q.Conversion && !q.Soft && q.Family is ProseQuantities.Time or ProseQuantities.Temperature or ProseQuantities.Length)
                .ToList();
            if (quantities.Count == 0) yield break;

            var words = WordPattern.Matches(prepared).Select(m => new Word(m.Value.ToLowerInvariant(), m.Index)).ToList();
            var texts = words.Select(w => w.Text).ToList();
            for (var i = 0; i < quantities.Count; i++)
            {
                var q = quantities[i];
                var (sentenceStart, sentenceEnd) = Sentence(prepared, q.Start);
                if (q.Family == ProseQuantities.Length)
                {
                    var dimensions = new List<ProseQuantity> { q };
                    while (i + 1 < quantities.Count && quantities[i + 1].Family == ProseQuantities.Length
                        && (quantities[i + 1].Start == dimensions[^1].Start
                            || (quantities[i + 1].Start >= dimensions[^1].End && DimensionJoin.IsMatch(prepared[dimensions[^1].End..quantities[i + 1].Start]))))
                    {
                        dimensions.Add(quantities[++i]);
                    }

                    var after = words.FirstOrDefault(w => w.Start >= dimensions[^1].End && w.Start < sentenceEnd && !IsUnitWord(w.Text));
                    if (after == null || !Vessels.Contains(after.Text)) continue;
                    yield return $"{Size(dimensions)} {after.Text}";
                    continue;
                }

                if (q.Family == ProseQuantities.Time && Stored(q, words, sentenceStart, sentenceEnd)) continue;
                var inOven = q.Family == ProseQuantities.Temperature
                    && words.Any(w => w.Start >= sentenceStart && w.Start < sentenceEnd && w.Text == "oven");
                var verb = inOven ? "oven" : VerbFor(words, texts, q, sentenceStart, sentenceEnd, verbs, q.Family == ProseQuantities.Temperature ? PanWork : null);
                var amount = q.Family == ProseQuantities.Time ? Time(q) : Temperature(q);
                yield return verb == null ? amount : $"{verb} {amount}";
            }
        }

        private static (int Start, int End) Sentence(string text, int at)
        {
            var start = 0;
            var end = text.Length;
            foreach (Match m in SentenceEnd.Matches(text))
            {
                if (m.Index < at && !IsDecimalPoint(text, m.Index)) start = m.Index + 1;
                else if (m.Index >= at && !IsDecimalPoint(text, m.Index))
                {
                    end = m.Index;
                    break;
                }
            }

            return (start, end);
        }

        private static bool IsDecimalPoint(string text, int index) =>
            text[index] == '.' && index > 0 && index + 1 < text.Length && char.IsDigit(text[index - 1]) && char.IsDigit(text[index + 1]);

        private static string? VerbFor(IReadOnlyList<Word> words, IReadOnlyList<string> texts, ProseQuantity q, int sentenceStart, int sentenceEnd, ProseVerbLexicon verbs, IReadOnlySet<string>? never = null)
        {
            for (var i = words.Count - 1; i >= 0; i--)
            {
                if (words[i].Start >= q.Start || words[i].Start < sentenceStart || never?.Contains(words[i].Text) == true) continue;
                if (verbs.GroupsAt(texts, i) is { } groups) return GroupName(groups, words[i].Text);
            }

            for (var i = 0; i < words.Count; i++)
            {
                if (words[i].Start < q.End || words[i].Start >= sentenceEnd || never?.Contains(words[i].Text) == true) continue;
                if (verbs.GroupsAt(texts, i) is { } groups) return GroupName(groups, words[i].Text);
            }

            return null;
        }

        /// <summary>Work on a pan or a dish, never what a temperature is for: "line 450°" read a pan lining as an oven (staging checkpoint 8).</summary>
        private static readonly HashSet<string> PanWork = new(StringComparer.Ordinal)
        {
            "line", "lines", "lined", "lining", "grease", "greased", "greasing", "butter", "buttered", "oil", "oiled", "spray", "sprayed",
            "flour", "floured", "cover", "covered", "wrap", "wrapped", "set", "place", "arrange", "arranged",
        };

        private static readonly HashSet<string> StorageWords = new(StringComparer.Ordinal)
        {
            "store", "stores", "stored", "storing", "storage", "leftover", "leftovers", "airtight",
        };

        private static readonly HashSet<string> KeepingWords = new(StringComparer.Ordinal) { "keep", "keeps", "kept", "last", "lasts" };

        private static readonly HashSet<string> ColdWords = new(StringComparer.Ordinal)
        {
            "chill", "chilled", "refrigerate", "refrigerated", "refrigerator", "fridge", "freeze", "frozen", "freezer", "cover", "covered",
        };

        private static readonly HashSet<string> LongUnits = new(StringComparer.Ordinal) { "day", "week", "month" };

        /// <summary>
        /// Whether a time is how long the dish keeps, not a step: a time after a word of storing it ("store", "airtight",
        /// "leftovers"), or a time of days or weeks in a sentence about storing or keeping ("keeps", "lasts") or bounded by "up
        /// to" in the cold ("cover and chill up to 4 days"). Staging checkpoint 8 found "cover 4 days" and "chill 2 weeks" among
        /// the key facts; a bake time before "storing" and "marinate up to 2 days" stay steps.
        /// </summary>
        private static bool Stored(ProseQuantity q, IReadOnlyList<Word> words, int sentenceStart, int sentenceEnd)
        {
            var sentence = words.Where(w => w.Start >= sentenceStart && w.Start < sentenceEnd).ToList();
            if (sentence.Any(w => StorageWords.Contains(w.Text) && w.Start < q.Start)) return true;
            if (!LongUnits.Contains(q.Unit)) return false;
            return sentence.Any(w => StorageWords.Contains(w.Text) || KeepingWords.Contains(w.Text))
                || (sentence.Any(w => ColdWords.Contains(w.Text))
                    && sentence.Zip(sentence.Skip(1)).Any(p => p.First.Text == "up" && p.Second.Text == "to" && p.Second.Start < q.Start));
        }

        private static string GroupName(IReadOnlySet<string> groups, string word)
        {
            if (groups.Contains(word) || !Inflected.IsMatch(word)) return word;
            var specific = groups.Where(g => !GeneralGroups.Contains(g)).OrderBy(g => g, StringComparer.Ordinal).FirstOrDefault();
            return specific ?? groups.OrderBy(g => g, StringComparer.Ordinal).First();
        }

        private static bool IsUnitWord(string word) => word is "inch" or "inches" or "in" or "cm" or "mm" or "centimeter" or "centimeters" or "centimetre" or "centimetres" or "foot" or "feet" or "x" or "by";

        private static string Time(ProseQuantity q)
        {
            if (q.Unit == "hour" && (q.Low * 60m % 1m == 0m) && (q.High * 60m % 1m == 0m) && (q.Low % 1m != 0m || q.High % 1m != 0m))
                return Time(q with { Low = q.Low * 60m, High = q.High * 60m, Unit = "minute" });
            var unit = q.Unit switch
            {
                "second" => "sec",
                "minute" => "min",
                "hour" => "hr",
                "day" => q.High == 1m ? "day" : "days",
                "week" => q.High == 1m ? "week" : "weeks",
                _ => q.Unit,
            };
            return $"{Range(q)} {unit}";
        }

        private static string Temperature(ProseQuantity q)
        {
            var scale = q.Unit switch
            {
                "F" => "°F",
                "C" => "°C",
                _ => "°",
            };
            return q.IsRange ? $"{Number(q.Low)}{scale} to {Number(q.High)}{scale}" : $"{Number(q.Low)}{scale}";
        }

        private static string Size(IReadOnlyList<ProseQuantity> dimensions)
        {
            var unit = dimensions[^1].Unit;
            var numbers = string.Join(" x ", dimensions.Select(Range));
            return unit switch
            {
                "inch" => $"{numbers}-inch",
                "foot" => $"{numbers}-foot",
                _ => $"{numbers} {unit}",
            };
        }

        private static string Range(ProseQuantity q) => q.IsRange ? $"{Number(q.Low)} to {Number(q.High)}" : Number(q.Low);

        private static string Number(decimal value)
        {
            var whole = decimal.Truncate(value);
            var part = value - whole;
            if (part == 0m) return whole.ToString(CultureInfo.InvariantCulture);
            var fraction = Fractions.FirstOrDefault(f => Math.Abs(f.Value - part) < 0.01m).Text;
            if (fraction == null) return value.ToString("0.##", CultureInfo.InvariantCulture);
            return whole == 0m ? fraction : $"{whole.ToString(CultureInfo.InvariantCulture)} {fraction}";
        }
    }
}

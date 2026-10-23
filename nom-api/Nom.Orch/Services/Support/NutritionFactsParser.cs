using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Nom.Data.Nutrition;
using Nom.Orch.Models.Shopping;

namespace Nom.Orch.Services.Support
{
    /// <summary>
    /// Reads a Nutrition Facts panel out of raw OCR text. Deterministic and offline: it only
    /// recognises what is printed, reports per-serving amounts as printed, and derives per-100 g
    /// values solely from an explicit gram weight. It never converts a volume to a mass, never
    /// turns a %DV into an amount, and never fills a missing value with a guess.
    /// </summary>
    public static class NutritionFactsParser
    {
        public const int ParserVersion = 1;

        private const string ServingKey = "_serving";
        private const string ServingsKey = "_servings";
        private const string CaloriesKey = "calories";
        private const string SaltKey = "salt";

        private static readonly (string[] Words, string Key)[] Labels =
        {
            (new[] { "of", "which", "saturates" }, "saturated_fat"),
            (new[] { "of", "which", "sugars" }, "sugars"),
            (new[] { "servings", "per", "container" }, ServingsKey),
            (new[] { "servings", "per", "package" }, ServingsKey),
            (new[] { "serving", "per", "container" }, ServingsKey),
            (new[] { "total", "carbohydrates" }, "carbohydrate"),
            (new[] { "total", "carbohydrate" }, "carbohydrate"),
            (new[] { "total", "carbs" }, "carbohydrate"),
            (new[] { "total", "carb" }, "carbohydrate"),
            (new[] { "saturated", "fat" }, "saturated_fat"),
            (new[] { "dietary", "fiber" }, "fiber"),
            (new[] { "dietary", "fibre" }, "fiber"),
            (new[] { "added", "sugars" }, "added_sugars"),
            (new[] { "added", "sugar" }, "added_sugars"),
            (new[] { "total", "sugars" }, "sugars"),
            (new[] { "serving", "size" }, ServingKey),
            (new[] { "serv", "size" }, ServingKey),
            (new[] { "trans", "fat" }, "trans_fat"),
            (new[] { "total", "fat" }, "fat"),
            (new[] { "sat", "fat" }, "saturated_fat"),
            (new[] { "vitamin", "d" }, "vitamin_d"),
            (new[] { "vit", "d" }, "vitamin_d"),
            (new[] { "vitamin", "a" }, "vitamin_a"),
            (new[] { "vitamin", "c" }, "vitamin_c"),
            (new[] { "carbohydrates" }, "carbohydrate"),
            (new[] { "carbohydrate" }, "carbohydrate"),
            (new[] { "cholesterol" }, "cholesterol"),
            (new[] { "cholest" }, "cholesterol"),
            (new[] { "saturates" }, "saturated_fat"),
            (new[] { "saturated" }, "saturated_fat"),
            (new[] { "potassium" }, "potassium"),
            (new[] { "potas" }, "potassium"),
            (new[] { "calories" }, CaloriesKey),
            (new[] { "servings" }, ServingsKey),
            (new[] { "calcium" }, "calcium"),
            (new[] { "protein" }, "protein"),
            (new[] { "sodium" }, "sodium"),
            (new[] { "energy" }, CaloriesKey),
            (new[] { "sugars" }, "sugars"),
            (new[] { "fiber" }, "fiber"),
            (new[] { "fibre" }, "fiber"),
            (new[] { "carbs" }, "carbohydrate"),
            (new[] { "trans" }, "trans_fat"),
            (new[] { "sugar" }, "sugars"),
            (new[] { "carb" }, "carbohydrate"),
            (new[] { "iron" }, "iron"),
            (new[] { "salt" }, SaltKey),
            (new[] { "fat" }, "fat"),
        };

        private static readonly Dictionary<string, string> DefaultUnits = new()
        {
            ["fat"] = "g", ["saturated_fat"] = "g", ["trans_fat"] = "g", ["cholesterol"] = "mg", ["sodium"] = "mg",
            ["carbohydrate"] = "g", ["fiber"] = "g", ["sugars"] = "g", ["added_sugars"] = "g", ["protein"] = "g",
            ["vitamin_d"] = "mcg", ["calcium"] = "mg", ["iron"] = "mg", ["potassium"] = "mg",
            ["vitamin_a"] = "mcg", ["vitamin_c"] = "mg", [SaltKey] = "g",
        };

        private static readonly string[] FooterMarkers =
        {
            "daily value", "%dv tells", "tells you", "2000 calories", "2,000 calories", "calories a day", "nutrition advice",
            "general nutrition", "contributes to a daily", "calorie diet",
        };

        private static readonly Regex ValueRegex = new(
            @"(?<lt>(?:<|less\s+than|under)\s*)?(?<num>\d+(?:\.\d+)?)\s*(?<unit>mcg|mg|kcal|kj|cal|ml|g|%)?(?![a-z0-9])",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex ColumnRegex = new(
            @"(per\s+(?:serving|container|package|pack|piece|bottle|can|100\s?g|100\s?ml|\d+(?:/\d+)?\s*[a-z]+)|as\s+packaged|as\s+prepared|as\s+sold)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex AmountPerServingRegex = new(@"amount\s+per\s+serving", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex Per100gRegex = new(@"(per|/)\s*100\s?g\b|\b100\s?g\s+(contains|provides|typical)|typical values per 100", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex GramsRegex = new(@"(?<![a-z0-9.])(\d+(?:\.\d+)?)\s*(?:g|gm|grams?)(?![a-z])", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex MlRegex = new(@"(?<![a-z0-9.])(\d+(?:\.\d+)?)\s*(?:ml|millilit(?:er|re)s?)(?![a-z])", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex ParentheticalRegex = new(@"\([^)]*\)", RegexOptions.Compiled);
        private static readonly Regex NumberTokenRegex = new(
            @"(?<![a-zA-Z0-9])(?<num>[0-9OoIl|SBZ]+(?:[.,][0-9OoIl|SBZ]+)?)\s*(?<unit>(?i:mcg|rncg|mcq|µg|ug|mg|rng|mq|rnq|kcal|kj|cal|g|%))(?![a-zA-Z0-9])",
            RegexOptions.Compiled);
        private static readonly Regex LineEndNumberRegex = new(@"(?<![a-zA-Z0-9])(?<num>[0-9][0-9OoIl|SBZ]*)\s*$", RegexOptions.Compiled);
        private static readonly Regex NotSignificantRegex = new(@"not\s+a\s+significant\s+source\s+of", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex OnlyPunctuationRegex = new(@"^[\W_]*$", RegexOptions.Compiled);
        private static readonly Regex WordRegex = new(@"[a-zA-Z]+", RegexOptions.Compiled);
        private static readonly Regex TrailingCountRegex = new(@"(about\s+|approx\.?\s+|~\s*)?\d+(?:\.\d+)?\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static NutritionFactsParseResult Parse(string? ocrText)
        {
            var result = new NutritionFactsParseResult();
            if (string.IsNullOrWhiteSpace(ocrText))
                return result;

            var lines = Segments(ocrText);
            var readings = new Dictionary<string, NutrientReading>();
            int? per100gColumn = null;
            var sawPer100g = false;
            var sawNutrientLine = false;

            for (var i = 0; i < lines.Count; i++)
            {
                var line = lines[i];

                var notSignificant = NotSignificantRegex.Match(line);
                if (notSignificant.Success)
                {
                    var tail = line.Substring(notSignificant.Index + notSignificant.Length);
                    foreach (var absent in FindLabels(Words(tail)).Select(l => l.Key).Where(k => DefaultUnits.ContainsKey(k) && k != SaltKey))
                        if (!result.NotSignificantSource.Contains(absent)) result.NotSignificantSource.Add(absent);
                    result.MatchedLines.Add(line);
                    continue;
                }

                var columns = ColumnRegex.Matches(AmountPerServingRegex.Replace(line, " ")).Cast<Match>().Select(m => m.Value.Trim()).ToList();
                if (columns.Count >= 2)
                {
                    result.DualColumn = true;
                    result.SecondColumnLabel = columns[1];
                    var idx = columns.FindIndex(c => Regex.IsMatch(c, @"100\s?g", RegexOptions.IgnoreCase));
                    if (idx >= 0) { per100gColumn = idx; sawPer100g = true; }
                    result.MatchedLines.Add(line);
                    continue;
                }
                if (Per100gRegex.IsMatch(line) && !sawNutrientLine)
                    sawPer100g = true;

                var words = Words(line);
                var label = FindLabel(words, 0);
                if (label == null)
                    continue;

                var (key, startWord, endWord) = label.Value;
                var rest = TextOutsideWords(line, words, startWord, endWord);

                if (key == ServingKey)
                {
                    rest = line.Substring(words[endWord].End).Trim();
                    if (string.IsNullOrWhiteSpace(rest) && i + 1 < lines.Count && FindLabel(Words(lines[i + 1]), 0) == null)
                        rest = lines[++i];
                    ParseServing(rest, result);
                    result.MatchedLines.Add(line);
                    continue;
                }

                if (key == ServingsKey)
                {
                    var servings = ParseServings(line, rest, out var approx);
                    if (servings != null)
                    {
                        result.ServingsPerContainer = servings;
                        result.ServingsPerContainerApprox = approx;
                        result.MatchedLines.Add(line);
                    }
                    continue;
                }

                if (key == CaloriesKey)
                {
                    if (endWord + 1 < words.Count && words[endWord + 1].Text == "from")
                        continue;
                    if (result.Calories != null)
                        continue;
                    var calories = ParseCalories(line, words, endWord, result);
                    if (calories == null && i + 1 < lines.Count && FindLabel(Words(lines[i + 1]), 0) == null)
                    {
                        calories = ParseCalories(lines[i + 1], Words(lines[i + 1]), -1, result);
                        if (calories != null) i++;
                    }
                    if (calories != null)
                    {
                        result.Calories = calories;
                        result.MatchedLines.Add(line);
                        sawNutrientLine = true;
                    }
                    continue;
                }

                if (!DefaultUnits.ContainsKey(key))
                    continue;

                var values = ExtractValues(rest);
                if (values.Count == 0 && i + 1 < lines.Count && FindLabel(Words(lines[i + 1]), 0) == null)
                {
                    values = ExtractValues(lines[i + 1]);
                    if (values.Count > 0) i++;
                }
                var groups = Group(values);
                if (groups.Count == 0)
                    continue;
                if (groups.Count == 1 && groups[0].Pdv == null && groups[0].Amount != null && i + 1 < lines.Count && IsLonePercent(lines[i + 1], out var pdv))
                {
                    groups[0].Pdv = pdv;
                    i++;
                }
                if (groups.Count >= 2)
                    result.DualColumn = true;

                var chosen = per100gColumn is { } col && col < groups.Count ? groups[col] : groups[0];
                var reading = new NutrientReading
                {
                    Key = key,
                    Amount = chosen.LessThan ? null : chosen.Amount,
                    LessThan = chosen.LessThan ? chosen.Amount : null,
                    Unit = chosen.Unit ?? DefaultUnits[key],
                    PercentDailyValue = chosen.Pdv,
                    Raw = line,
                };
                if (!readings.ContainsKey(key))
                {
                    readings[key] = reading;
                    result.MatchedLines.Add(line);
                    sawNutrientLine = true;
                }
            }

            if (readings.TryGetValue(SaltKey, out var salt))
            {
                readings.Remove(SaltKey);
                if (!readings.ContainsKey("sodium") && salt.Amount is { } saltGrams && salt.Unit == "g")
                {
                    readings["sodium"] = new NutrientReading
                    {
                        Key = "sodium",
                        Amount = Math.Round(saltGrams * 400m, 2),
                        Unit = "mg",
                        Raw = salt.Raw,
                    };
                    result.Warnings.Add("sodium_derived_from_salt");
                }
            }

            result.Nutrients = readings.Values.ToList();
            var fields = result.Nutrients.Count + (result.Calories != null ? 1 : 0);
            result.Basis = sawPer100g ? NutritionBasis.Per100g
                : fields > 0 ? NutritionBasis.Serving
                : NutritionBasis.Unknown;

            Assess(result);
            return result;
        }

        /// <summary>
        /// Recomputes everything that follows from the readings: the per-100 g view (only from an
        /// explicit gram weight), the consistency warnings, and the parsed/confidence summary.
        /// Used after parsing and again after a user corrects the values.
        /// </summary>
        public static void Assess(NutritionFactsParseResult result)
        {
            result.FieldsRecognised = result.Nutrients.Count + (result.Calories != null ? 1 : 0);
            result.Per100g = null;
            result.Warnings = result.Warnings.Where(w => w == "calories_converted_from_kj" || w == "sodium_derived_from_salt").ToList();

            DerivePer100g(result);
            AddConsistencyWarnings(result);

            var servingKnown = result.ServingGrams != null || result.ServingMl != null || result.ServingText != null;
            result.Parsed = result.FieldsRecognised >= 3 || (result.Calories != null && servingKnown);
            var confidence = Math.Min(1m, result.FieldsRecognised / 15m) - 0.05m * result.Warnings.Count;
            result.Confidence = Math.Round(Math.Max(0m, confidence), 2);
        }

        private static List<string> Segments(string text)
        {
            var normalised = text.Normalize(NormalizationForm.FormKC)
                .Replace('⁄', '/')
                .Replace(' ', ' ')
                .Replace(' ', ' ')
                .Replace(' ', ' ');
            normalised = Regex.Replace(normalised, @"°/[oO0]", "%");
            normalised = Regex.Replace(normalised, @"%[oO](?![a-z])", "%");

            var segments = new List<string>();
            foreach (var raw in normalised.Split('\n'))
            {
                var line = Regex.Replace(raw.Trim(), @"[ \t]+", " ");
                if (line.Length == 0 || OnlyPunctuationRegex.IsMatch(line))
                    continue;
                var lower = line.ToLowerInvariant();
                if (lower.StartsWith("ingredients", StringComparison.Ordinal) || lower.StartsWith("ingredlents", StringComparison.Ordinal))
                    break;
                if (FooterMarkers.Any(m => lower.Contains(m)))
                    continue;
                segments.AddRange(SplitAtLabels(FixNumericTokens(line)));
            }
            return segments;
        }

        private static IEnumerable<string> SplitAtLabels(string line)
        {
            var words = Words(line);
            var labels = FindLabels(words);
            if (labels.Count <= 1 || NotSignificantRegex.IsMatch(line))
            {
                yield return line;
                yield break;
            }

            var cuts = new List<int>();
            foreach (var (key, start, _) in labels.Skip(1))
            {
                var cutWord = start;
                if (start >= 2 && words[start - 2].Text is "includes" or "incl" or "including")
                    cutWord = start - 2;
                var cut = words[cutWord].Start;
                if (key == ServingsKey && cutWord > 0)
                {
                    var between = line.Substring(words[cutWord - 1].End, cut - words[cutWord - 1].End);
                    var count = TrailingCountRegex.Match(between);
                    if (count.Success) cut = words[cutWord - 1].End + count.Index;
                }
                cuts.Add(cut);
            }

            var from = 0;
            foreach (var cut in cuts.Distinct().OrderBy(c => c))
            {
                if (cut <= from) continue;
                var piece = line.Substring(from, cut - from).Trim(' ', ',', ';', '|');
                if (piece.Length > 0) yield return piece;
                from = cut;
            }
            var last = line.Substring(from).Trim(' ', ',', ';', '|');
            if (last.Length > 0) yield return last;
        }

        private static string FixNumericTokens(string line)
        {
            var fixedLine = NumberTokenRegex.Replace(line, m =>
            {
                var num = m.Groups["num"].Value;
                var unit = m.Groups["unit"].Value.ToLowerInvariant();
                var hasDigit = num.Any(char.IsDigit);
                var allZeroLike = num.All(c => c is 'O' or 'o');
                var allConfusable = num.All(c => "OoIl|SBZ".Contains(c));
                var massUnit = unit is "g" or "mg" or "mcg" or "rng" or "mq" or "rnq" or "rncg" or "mcq" or "µg" or "ug";
                if (!hasDigit && !(allZeroLike && num.Length <= 2) && !(allConfusable && num.Length <= 2 && massUnit))
                    return m.Value;

                var fixedUnit = unit switch
                {
                    "rng" or "mq" or "rnq" => "mg",
                    "rncg" or "mcq" or "µg" or "ug" => "mcg",
                    _ => unit,
                };
                return Digits(num) + fixedUnit;
            });
            return LineEndNumberRegex.Replace(fixedLine, m => Digits(m.Groups["num"].Value));
        }

        private static string Digits(string num)
        {
            var digits = new StringBuilder();
            foreach (var c in num)
            {
                digits.Append(c switch
                {
                    'O' or 'o' => '0',
                    'I' or 'l' or '|' => '1',
                    'S' => '5',
                    'B' => '8',
                    'Z' => '2',
                    _ => c,
                });
            }
            return Regex.Replace(digits.ToString(), @",(\d{3})(?!\d)", "$1").Replace(',', '.');
        }

        private sealed record Word(string Text, int Start, int End);

        private static List<Word> Words(string line)
        {
            var list = new List<Word>();
            foreach (Match m in WordRegex.Matches(line))
                list.Add(new Word(m.Value.ToLowerInvariant(), m.Index, m.Index + m.Length));
            return list;
        }

        private static (string Key, int Start, int End)? FindLabel(List<Word> words, int from)
        {
            for (var start = from; start < words.Count; start++)
            {
                (string Key, int Start, int End)? best = null;
                foreach (var (phrase, key) in Labels)
                {
                    if (start + phrase.Length > words.Count) continue;
                    var ok = true;
                    for (var k = 0; k < phrase.Length && ok; k++)
                        ok = WordMatches(words[start + k].Text, phrase[k]);
                    if (!ok) continue;
                    var end = start + phrase.Length - 1;
                    if (best == null || end > best.Value.End)
                        best = (key, start, end);
                }
                if (best != null) return best;
            }
            return null;
        }

        private static List<(string Key, int Start, int End)> FindLabels(List<Word> words)
        {
            var found = new List<(string Key, int Start, int End)>();
            var from = 0;
            while (FindLabel(words, from) is { } label)
            {
                if (label.Start == 0 || words[label.Start - 1].Text != "from")
                    found.Add(label);
                from = label.End + 1;
            }
            return found;
        }

        private static bool WordMatches(string word, string target)
        {
            if (word == target) return true;
            if (target.Length <= 4) return false;
            var allowed = target.Length <= 6 ? 1 : 2;
            if (Math.Abs(word.Length - target.Length) > allowed) return false;
            return Levenshtein(word, target) <= allowed;
        }

        private static int Levenshtein(string a, string b)
        {
            var prev = new int[b.Length + 1];
            var cur = new int[b.Length + 1];
            for (var j = 0; j <= b.Length; j++) prev[j] = j;
            for (var i = 1; i <= a.Length; i++)
            {
                cur[0] = i;
                for (var j = 1; j <= b.Length; j++)
                {
                    var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
                }
                (prev, cur) = (cur, prev);
            }
            return prev[b.Length];
        }

        private static string TextOutsideWords(string line, List<Word> words, int startWord, int endWord)
        {
            var before = line.Substring(0, words[startWord].Start);
            var after = line.Substring(words[endWord].End);
            return (before + " " + after).Trim();
        }

        private sealed class Value
        {
            public decimal Number { get; init; }
            public string? Unit { get; init; }
            public bool LessThan { get; init; }
        }

        private sealed class ValueGroup
        {
            public decimal? Amount { get; set; }
            public string? Unit { get; set; }
            public bool LessThan { get; set; }
            public int? Pdv { get; set; }
        }

        private static List<Value> ExtractValues(string text)
        {
            var list = new List<Value>();
            foreach (Match m in ValueRegex.Matches(text))
            {
                if (!decimal.TryParse(m.Groups["num"].Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number))
                    continue;
                var unit = m.Groups["unit"].Success ? m.Groups["unit"].Value.ToLowerInvariant() : null;
                if (unit == "ml") continue;
                list.Add(new Value { Number = number, Unit = unit, LessThan = m.Groups["lt"].Success });
            }
            return list;
        }

        private static List<ValueGroup> Group(List<Value> values)
        {
            var groups = new List<ValueGroup>();
            ValueGroup? open = null;
            foreach (var v in values)
            {
                if (v.Unit == "%")
                {
                    if (open != null && open.Pdv == null)
                        open.Pdv = (int)Math.Round(v.Number);
                    else
                        groups.Add(new ValueGroup { Pdv = (int)Math.Round(v.Number) });
                    open = null;
                    continue;
                }
                if (v.Unit is "kcal" or "kj" or "cal") continue;
                open = new ValueGroup { Amount = v.Number, Unit = v.Unit, LessThan = v.LessThan };
                groups.Add(open);
            }
            return groups;
        }

        private static bool IsLonePercent(string line, out int pdv)
        {
            pdv = 0;
            var m = Regex.Match(line.Trim(), @"^(\d+)\s*%(\s*dv\*?)?$", RegexOptions.IgnoreCase);
            if (!m.Success) return false;
            pdv = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            return true;
        }

        private static decimal? ParseCalories(string line, List<Word> words, int endWord, NutritionFactsParseResult result)
        {
            var from = endWord >= 0 && endWord < words.Count ? words[endWord].End : 0;
            var after = ExtractValues(line.Substring(from)).Where(v => !v.LessThan).ToList();
            var kcal = after.FirstOrDefault(v => v.Unit is "kcal" or "cal");
            if (kcal != null) return kcal.Number;
            var bare = after.FirstOrDefault(v => v.Unit == null);
            if (bare != null) return bare.Number;
            var kj = after.FirstOrDefault(v => v.Unit == "kj");
            if (kj != null)
            {
                result.Warnings.Add("calories_converted_from_kj");
                return Math.Round(kj.Number / 4.184m);
            }
            if (endWord >= 0 && words[endWord].Start > 0)
            {
                var before = ExtractValues(line.Substring(0, words[endWord].Start)).LastOrDefault(v => v.Unit == null);
                if (before != null) return before.Number;
            }
            return null;
        }

        private static void ParseServing(string rest, NutritionFactsParseResult result)
        {
            if (string.IsNullOrWhiteSpace(rest)) return;
            var text = rest.Trim().TrimStart(':', '-', ' ');

            var grams = GramsRegex.Match(text);
            if (grams.Success && result.ServingGrams == null)
                result.ServingGrams = decimal.Parse(grams.Groups[1].Value, CultureInfo.InvariantCulture);
            var ml = MlRegex.Match(text);
            if (ml.Success && result.ServingMl == null)
                result.ServingMl = decimal.Parse(ml.Groups[1].Value, CultureInfo.InvariantCulture);

            if (result.ServingText != null) return;
            var label = ParentheticalRegex.Replace(text, " ");
            label = GramsRegex.Replace(label, " ");
            label = MlRegex.Replace(label, " ");
            label = Regex.Replace(label, @"\s+", " ").Trim(' ', ',', ';', '/', '-', '.', '|', ':');
            if (label.Length > 0)
                result.ServingText = label;
            else if (result.ServingGrams is { } g)
                result.ServingText = $"{g.ToString(CultureInfo.InvariantCulture)} g";
            else if (result.ServingMl is { } v)
                result.ServingText = $"{v.ToString(CultureInfo.InvariantCulture)} mL";
        }

        private static decimal? ParseServings(string line, string rest, out bool approx)
        {
            var lower = line.ToLowerInvariant();
            approx = lower.Contains("about") || lower.Contains("approx") || lower.Contains('~');
            return ExtractValues(rest).FirstOrDefault(v => v.Unit == null)?.Number;
        }

        private static void DerivePer100g(NutritionFactsParseResult result)
        {
            if (result.Basis == NutritionBasis.Per100g)
            {
                result.Per100g = new Per100gNutrition
                {
                    Calories = result.Calories,
                    Nutrients = result.Nutrients.Where(n => n.Amount != null)
                        .Select(n => new Per100gReading { Key = n.Key, Amount = n.Amount!.Value, Unit = n.Unit }).ToList(),
                };
                return;
            }
            if (result.Basis != NutritionBasis.Serving || result.ServingGrams is not { } grams || grams <= 0)
                return;

            result.Per100g = new Per100gNutrition
            {
                Calories = result.Calories is { } kcal ? Math.Round(kcal * 100m / grams, 2) : null,
                Nutrients = result.Nutrients.Where(n => n.Amount != null)
                    .Select(n => new Per100gReading { Key = n.Key, Amount = Math.Round(n.Amount!.Value * 100m / grams, 2), Unit = n.Unit }).ToList(),
            };
        }

        private static void AddConsistencyWarnings(NutritionFactsParseResult result)
        {
            decimal? Amount(string key) => result.Nutrients.FirstOrDefault(n => n.Key == key)?.Amount;

            var fat = Amount("fat");
            var sat = Amount("saturated_fat");
            var trans = Amount("trans_fat");
            var carb = Amount("carbohydrate");
            var fiber = Amount("fiber");
            var sugars = Amount("sugars");
            var added = Amount("added_sugars");

            if (fat != null && (sat ?? 0) + (trans ?? 0) > fat + 0.5m) result.Warnings.Add("saturated_plus_trans_exceeds_fat");
            if (carb != null && (fiber ?? 0) + (sugars ?? 0) > carb + 0.5m) result.Warnings.Add("fiber_plus_sugars_exceeds_carbohydrate");
            if (sugars != null && added != null && added > sugars + 0.5m) result.Warnings.Add("added_sugars_exceeds_sugars");

            if (result.Calories != null && result.Basis == NutritionBasis.Serving && result.ServingGrams == null)
                result.Warnings.Add("serving_weight_needed_for_per_100g");

            if (result.Per100g == null) return;
            decimal? Per100(string key) => result.Per100g.Nutrients.FirstOrDefault(n => n.Key == key)?.Amount;

            if (result.Per100g.Calories is { } kcal100 && kcal100 > 900m) result.Warnings.Add("per100g_calories_impossible");
            var p = Per100("protein"); var c = Per100("carbohydrate"); var f = Per100("fat");
            if (p != null && c != null && f != null && p + c + f > 105m) result.Warnings.Add("per100g_macro_sum_impossible");

            if (result.Per100g.Calories != null && p != null && c != null && f != null)
            {
                var check = new FoodDataQualityValidator().Validate(new FoodQualityInput("label", result.Per100g.Calories, p, c, f));
                foreach (var reason in check.Reasons.Where(r => !r.StartsWith("name_", StringComparison.Ordinal) && !result.Warnings.Contains(r)))
                    result.Warnings.Add(reason);
            }
        }
    }
}

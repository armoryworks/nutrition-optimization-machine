using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;

namespace Nom.Orch.Services.Support
{
    /// <summary>
    /// One amount a recipe text states: a value or a range, its unit family (time, temperature,
    /// length, volume, mass or count) and unit, and where it stands in the prepared text.
    /// <see cref="Low"/> and <see cref="High"/> are as written in <see cref="Unit"/>; a range
    /// collapsed or widened changes them. A count of exactly one, or a spelled-out count in a soft
    /// context ("all four sides", "two equal parts"), is <see cref="Soft"/>: it is never required
    /// and never counts as added. A <see cref="Conversion"/> is an amount in brackets right after
    /// another of its family ("350°F (175°C)", "9-inch (23 cm)"): it may be dropped.
    /// </summary>
    public sealed record ProseQuantity(decimal Low, decimal High, string Family, string Unit, int Start, int End, string Text, bool Conversion, bool Soft)
    {
        public bool IsRange => Low != High;

        public override string ToString() => Text;
    }

    /// <summary>
    /// Reads the amounts a recipe method states, each with its unit and range, so two texts can be
    /// compared by quantity rather than by bare number. "a" or "an" before a unit is 1, "a minute
    /// or two" is 1 to 2 minutes, "a half hour" and "half an hour" are 30 minutes, "1 and 1/2" and
    /// "one and a half" are 1.5, "1 hour 30 minutes" is one amount of 90 minutes, and "9 x 13-inch"
    /// is two lengths. Step markers ("2.", "Step 3", "2In a bowl") and cross-references ("as in
    /// step 3", "No. 156") are not amounts. Two amounts are equal only when they are of one family
    /// and agree within 2 % at both ends once converted (¼ lb is 4 oz, 350 °F is 175 °C), so a
    /// unit swapped inside a family (tablespoon to teaspoon) or a range collapsed or widened is a
    /// different amount. Text only: no database and no model.
    /// </summary>
    public static class ProseQuantities
    {
        public const string Time = "time";
        public const string Temperature = "temperature";
        public const string Length = "length";
        public const string Volume = "volume";
        public const string Mass = "mass";
        public const string Count = "count";

        public const decimal Tolerance = 0.02m;

        private const string Glyphs = "¼½¾⅓⅔⅕⅖⅗⅘⅙⅚⅛⅜⅝⅞";

        private static readonly Dictionary<char, decimal> GlyphValues = new()
        {
            ['¼'] = 0.25m, ['½'] = 0.5m, ['¾'] = 0.75m, ['⅓'] = 1m / 3m, ['⅔'] = 2m / 3m,
            ['⅕'] = 0.2m, ['⅖'] = 0.4m, ['⅗'] = 0.6m, ['⅘'] = 0.8m, ['⅙'] = 1m / 6m, ['⅚'] = 5m / 6m,
            ['⅛'] = 0.125m, ['⅜'] = 0.375m, ['⅝'] = 0.625m, ['⅞'] = 0.875m,
        };

        private sealed record UnitInfo(string Family, string Unit, decimal Factor);

        private static readonly (string[] Forms, string Family, string Unit, decimal Factor)[] UnitTable =
        {
            (new[] { "seconds", "second", "secs", "sec" }, Time, "second", 1m / 60m),
            (new[] { "minutes", "minute", "mins", "min" }, Time, "minute", 1m),
            (new[] { "hours", "hour", "hrs", "hr" }, Time, "hour", 60m),
            (new[] { "days", "day" }, Time, "day", 1440m),
            (new[] { "weeks", "week" }, Time, "week", 10080m),
            (new[] { "inches", "inch" }, Length, "inch", 1m),
            (new[] { "feet", "foot" }, Length, "foot", 12m),
            (new[] { "centimeters", "centimetres", "centimeter", "centimetre", "cm" }, Length, "cm", 1m / 2.54m),
            (new[] { "millimeters", "millimetres", "millimeter", "millimetre", "mm" }, Length, "mm", 1m / 25.4m),
            (new[] { "teaspoonfuls", "teaspoonful", "teaspoons", "teaspoon", "tsps", "tsp" }, Volume, "teaspoon", 1m),
            (new[] { "tablespoonfuls", "tablespoonful", "tablespoons", "tablespoon", "tbsps", "tbsp", "tblsp", "tbs", "tbl" }, Volume, "tablespoon", 3m),
            (new[] { "dessertspoonfuls", "dessertspoonful", "dessertspoons", "dessertspoon" }, Volume, "dessertspoon", 2m),
            (new[] { "saltspoonfuls", "saltspoonful", "saltspoons", "saltspoon" }, Volume, "saltspoon", 0.25m),
            (new[] { "cupfuls", "cupful", "cups", "cup" }, Volume, "cup", 48m),
            (new[] { "teacupfuls", "teacupful", "teacups", "teacup" }, Volume, "teacup", 24m),
            (new[] { "wineglassfuls", "wineglassful", "wineglasses", "wineglass" }, Volume, "wineglass", 12m),
            (new[] { "gills", "gill" }, Volume, "gill", 24m),
            (new[] { "pints", "pint", "pts", "pt" }, Volume, "pint", 96m),
            (new[] { "quarts", "quart", "qts", "qt" }, Volume, "quart", 192m),
            (new[] { "gallons", "gallon", "gal" }, Volume, "gallon", 768m),
            (new[] { "milliliters", "millilitres", "milliliter", "millilitre", "ml" }, Volume, "ml", 0.202884m),
            (new[] { "liters", "litres", "liter", "litre" }, Volume, "liter", 202.884m),
            (new[] { "ounces", "ounce", "ozs", "oz" }, Mass, "ounce", 1m),
            (new[] { "pounds", "pound", "lbs", "lb" }, Mass, "pound", 16m),
            (new[] { "grams", "gram", "grammes", "gramme", "gr", "g" }, Mass, "gram", 0.035274m),
            (new[] { "kilograms", "kilogram", "kilos", "kilo", "kgs", "kg" }, Mass, "kilogram", 35.274m),
            (new[] { "pinches", "pinch", "dashes", "dash" }, Count, "pinch", 1m),
            (new[] { "cloves", "clove" }, Count, "clove", 1m),
            (new[] { "slices", "slice" }, Count, "slice", 1m),
            (new[] { "cans", "can", "tins", "tin" }, Count, "can", 1m),
            (new[] { "packages", "package", "packets", "packet", "jars", "jar" }, Count, "package", 1m),
            (new[] { "sticks", "stick", "sprigs", "sprig", "handfuls", "handful", "pieces", "piece" }, Count, "piece", 1m),
        };

        private static readonly Dictionary<string, UnitInfo> Units = UnitTable
            .SelectMany(u => u.Forms.Select(f => (Form: f, Info: new UnitInfo(u.Family, u.Unit, u.Factor))))
            .ToDictionary(x => x.Form, x => x.Info, StringComparer.OrdinalIgnoreCase);


        private static readonly Dictionary<string, decimal> NumberWords = new(StringComparer.OrdinalIgnoreCase)
        {
            ["one"] = 1m, ["two"] = 2m, ["three"] = 3m, ["four"] = 4m, ["five"] = 5m, ["six"] = 6m, ["seven"] = 7m, ["eight"] = 8m,
            ["nine"] = 9m, ["ten"] = 10m, ["eleven"] = 11m, ["twelve"] = 12m, ["thirteen"] = 13m, ["fourteen"] = 14m, ["fifteen"] = 15m,
            ["sixteen"] = 16m, ["seventeen"] = 17m, ["eighteen"] = 18m, ["nineteen"] = 19m, ["twenty"] = 20m, ["thirty"] = 30m,
            ["forty"] = 40m, ["fifty"] = 50m, ["sixty"] = 60m, ["seventy"] = 70m, ["eighty"] = 80m, ["ninety"] = 90m, ["dozen"] = 12m,
        };

        private static readonly Dictionary<string, decimal> Denominators = new(StringComparer.OrdinalIgnoreCase)
        {
            ["half"] = 2m, ["halves"] = 2m, ["third"] = 3m, ["thirds"] = 3m, ["quarter"] = 4m, ["quarters"] = 4m,
            ["fourth"] = 4m, ["fourths"] = 4m, ["eighth"] = 8m, ["eighths"] = 8m,
        };

        private static readonly HashSet<string> Qualifiers = new(StringComparer.OrdinalIgnoreCase)
        {
            "heaping", "heaped", "level", "scant", "generous", "rounded", "good", "full", "large", "small", "medium", "extra", "whole", "more", "additional",
        };

        private static readonly HashSet<string> SoftContext = new(StringComparer.OrdinalIgnoreCase)
        {
            "sides", "side", "corners", "corner", "edges", "edge", "ends", "end", "equal",
        };

        private static readonly Regex Tags = new(@"<[^>]+>", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private const string ReferenceAbbreviations = @"(?-i:No|Nos|N|Nr|Art|Arts|Fig|Figs|Vol|Chap|Ch|no|nos|nr|art|arts|vol|chap|ch|pp|p)";

        private static readonly Regex StepMarker = new(
            @"(?<=^|(?<!\b(?:" + ReferenceAbbreviations + @"))[.!?:]\s*)\s*(?:step\s*)?\d{1,2}\s*[.):](?=\s*\p{L})|^\s*\d{1,2}(?=\p{Lu}\p{Ll})|(?<=(?<!\b(?:" + ReferenceAbbreviations + @"))[.!?])\s*\d{1,2}\.\s*$|^\s*step\s*\d{1,2}\b\s*[:.\-–]?",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex Reference = new(
            @"\bsteps?\s+\d{1,2}(?:\s*(?:and|or|to|-|–|,)\s*\d{1,2})*\b|\b(?:" + ReferenceAbbreviations + @")\.\s*\d+(?:\s*(?:and|or|,)\s*\d+)*\b|\b(?:recipe|page|p)\.?\s+\d+\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex TokenPattern = new(
            @"(?<num>\d+\s?[" + Glyphs + @"]|\d+(?:\s+|-)\d+/\d+(?!\d)|\d+/\d+(?!\d)|\d{1,3}(?:,\d{3})+(?!\d)|\d+(?:\.\d+)?|[" + Glyphs + @"])"
            + @"|(?<word>\p{L}+)|(?<sym>[°º×\-–—()])",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly char[] MixedSeparators = { ' ', '-' };

        private enum Kind { Number, Word, Symbol }

        private sealed record Token(Kind Kind, string Text, int Start, int End, decimal Value, bool Bracketed);

        /// <summary>The text with markup removed, entities decoded, step markers dropped and cross-references read as the word "ref".</summary>
        public static string Prepare(string? text)
        {
            var clean = Regex.Replace(WebUtility.HtmlDecode(Tags.Replace(text ?? string.Empty, " ")), @"\s+", " ").Trim()
                .Replace('⁄', '/');
            return Reference.Replace(StepMarker.Replace(clean, " "), " ref ").Trim();
        }

        /// <summary>Whether a step holds a step marker of its own ("2. Stir", "Step 3:", "2In a bowl", a trailing "3.").</summary>
        public static bool HasStepMarker(string? text)
        {
            var clean = Regex.Replace(WebUtility.HtmlDecode(Tags.Replace(text ?? string.Empty, " ")), @"\s+", " ").Trim();
            return StepMarker.IsMatch(clean);
        }

        /// <summary>The text with every step marker <see cref="HasStepMarker"/> finds taken out and the spaces around it closed up; nothing else changes.</summary>
        public static string StripStepMarkers(string? text)
        {
            var source = text ?? string.Empty;
            var stripped = StepMarker.Replace(source, " ");
            return stripped == source ? source : Regex.Replace(stripped, @"[ \t]{2,}", " ").Trim();
        }

        /// <summary>Every amount <paramref name="text"/> states, in order, with positions in <see cref="Prepare"/>'s text.</summary>
        public static IReadOnlyList<ProseQuantity> Read(string? text) => ReadPrepared(Prepare(text));

        /// <summary>Every amount a text <see cref="Prepare"/> has already prepared states, with positions in that text.</summary>
        public static IReadOnlyList<ProseQuantity> ReadPrepared(string prepared)
        {
            var tokens = Tokens(prepared);
            var found = new List<ProseQuantity>();
            var i = 0;
            while (i < tokens.Count)
            {
                var next = ReadAt(tokens, i, prepared, found);
                i = next > i ? next : i + 1;
            }

            return Conversions(Merge(found, prepared), prepared);
        }

        /// <summary>A prepared text with the span of every amount in <paramref name="quantities"/> replaced by one space; amounts that share or overlap a span ("12 x 18 inches" is two lengths over one span) are cut once.</summary>
        public static string WithoutAmounts(string prepared, IEnumerable<ProseQuantity> quantities)
        {
            var text = prepared;
            var cut = prepared.Length;
            foreach (var q in quantities.OrderByDescending(q => q.Start).ThenByDescending(q => q.End))
            {
                var end = Math.Min(q.End, cut);
                if (q.Start < 0 || q.Start >= end) continue;
                text = text[..q.Start] + " " + text[end..];
                cut = q.Start;
            }

            return text;
        }

        /// <summary>Whether two amounts are the same: one family, and both ends within <see cref="Tolerance"/> once in one unit.</summary>
        public static bool Equal(ProseQuantity a, ProseQuantity b)
        {
            if (a.Family != b.Family) return false;
            if (a.Family == Temperature) return SameTemperature(a, b);
            return Close(Base(a.Low, a), Base(b.Low, b)) && Close(Base(a.High, a), Base(b.High, b));
        }

        /// <summary>The amount in the base unit of its family: minutes, inches, teaspoons, ounces, °F or a plain count.</summary>
        public static decimal Base(decimal value, ProseQuantity q) =>
            q.Family == Temperature ? (q.Unit == "C" ? value * 9m / 5m + 32m : value) : value * FactorOf(q.Unit);

        /// <summary>Whether two amounts state the same numbers in different units of one family ("½ tablespoon" and "½ teaspoon").</summary>
        public static bool SameNumbersOtherUnit(ProseQuantity a, ProseQuantity b) =>
            a.Family == b.Family && a.Unit != b.Unit && Close(a.Low, b.Low) && Close(a.High, b.High);

        /// <summary>Whether two amounts of one family, one or both a range, overlap: a range collapsed, widened or narrowed.</summary>
        public static bool RangeChanged(ProseQuantity a, ProseQuantity b)
        {
            if (a.Family != b.Family || (!a.IsRange && !b.IsRange) || a.Family == Temperature) return false;
            var (al, ah) = Ordered(Base(a.Low, a), Base(a.High, a));
            var (bl, bh) = Ordered(Base(b.Low, b), Base(b.High, b));
            return al <= bh * (1 + Tolerance) && bl <= ah * (1 + Tolerance);
        }

        private static (decimal, decimal) Ordered(decimal x, decimal y) => x <= y ? (x, y) : (y, x);

        private static decimal FactorOf(string unit) =>
            UnitTable.FirstOrDefault(u => u.Unit == unit).Factor is var f && f != 0 ? f : 1m;

        private static bool Close(decimal x, decimal y) =>
            x == y || Math.Abs(x - y) <= Tolerance * Math.Max(Math.Abs(x), Math.Abs(y));

        private static bool SameTemperature(ProseQuantity a, ProseQuantity b)
        {
            if (a.Unit == "degree" || b.Unit == "degree") return Close(a.Low, b.Low) && Close(a.High, b.High);
            return Close(Base(a.Low, a), Base(b.Low, b)) && Close(Base(a.High, a), Base(b.High, b));
        }

        private static List<Token> Tokens(string text)
        {
            var depth = new int[text.Length + 1];
            var level = 0;
            for (var c = 0; c < text.Length; c++)
            {
                if (text[c] == '(') level++;
                depth[c] = level;
                if (text[c] == ')' && level > 0) level--;
            }

            var tokens = new List<Token>();
            foreach (Match m in TokenPattern.Matches(text))
            {
                var bracketed = depth[m.Index] > 0;
                if (m.Groups["num"].Success) tokens.Add(new Token(Kind.Number, m.Value, m.Index, m.Index + m.Length, NumberValue(m.Value), bracketed));
                else if (m.Groups["word"].Success) tokens.Add(new Token(Kind.Word, m.Value, m.Index, m.Index + m.Length, 0m, bracketed));
                else tokens.Add(new Token(Kind.Symbol, m.Value, m.Index, m.Index + m.Length, 0m, bracketed));
            }

            return tokens;
        }

        private static decimal NumberValue(string text)
        {
            text = text.Trim();
            var last = text[^1];
            if (GlyphValues.TryGetValue(last, out var glyph))
            {
                var whole = text[..^1].Trim();
                return (whole.Length > 0 ? decimal.Parse(whole, CultureInfo.InvariantCulture) : 0m) + glyph;
            }

            if (text.Contains('/'))
            {
                var parts = text.Split(MixedSeparators, StringSplitOptions.RemoveEmptyEntries);
                var whole = parts.Length > 1 ? decimal.Parse(parts[0], CultureInfo.InvariantCulture) : 0m;
                var fraction = parts[^1].Split('/');
                var denominator = decimal.Parse(fraction[1], CultureInfo.InvariantCulture);
                return whole + (denominator == 0 ? 0m : decimal.Parse(fraction[0], CultureInfo.InvariantCulture) / denominator);
            }

            return decimal.Parse(text.Replace(",", string.Empty), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
        }

        private static bool IsWord(List<Token> tokens, int i, params string[] words) =>
            i < tokens.Count && tokens[i].Kind == Kind.Word && words.Any(w => string.Equals(tokens[i].Text, w, StringComparison.OrdinalIgnoreCase));

        private static bool IsSymbol(List<Token> tokens, int i, params string[] symbols) =>
            i < tokens.Count && tokens[i].Kind == Kind.Symbol && symbols.Contains(tokens[i].Text);

        private sealed record Value(decimal Amount, int Next, bool Article, bool Spelled);

        private static Value? ReadValue(List<Token> tokens, int i, bool allowArticle = true)
        {
            if (i >= tokens.Count) return null;
            var t = tokens[i];
            Value? value = null;
            if (t.Kind == Kind.Number)
            {
                value = new Value(t.Value, i + 1, false, false);
            }
            else if (t.Kind == Kind.Word)
            {
                if (NumberWords.TryGetValue(t.Text, out var n))
                {
                    var next = i + 1;
                    if (n >= 20m && IsSymbol(tokens, next, "-") && next + 1 < tokens.Count && NumberWords.TryGetValue(tokens[next + 1].Text, out var ones) && ones < 10m)
                    {
                        n += ones;
                        next += 2;
                    }

                    var denominatorAt = IsSymbol(tokens, next, "-") ? next + 1 : next;
                    if (denominatorAt < tokens.Count && Denominators.TryGetValue(tokens[denominatorAt].Text, out var d) && n < d)
                    {
                        value = new Value(n / d, denominatorAt + 1, false, true);
                    }
                    else
                    {
                        value = new Value(n, next, false, true);
                    }
                }
                else if (IsWord(tokens, i, "a", "an") && allowArticle)
                {
                    if (i + 1 < tokens.Count && Denominators.TryGetValue(tokens[i + 1].Text, out var d) && !tokens[i + 1].Text.EndsWith('s'))
                        value = new Value(1m / d, i + 2, false, true);
                    else
                        value = new Value(1m, i + 1, true, true);
                }
                else if (IsWord(tokens, i, "half", "quarter"))
                {
                    var d = Denominators[t.Text];
                    var next = IsWord(tokens, i + 1, "a", "an") ? i + 2 : i + 1;
                    if (IsWord(tokens, next, "of") && IsWord(tokens, next + 1, "a", "an")) next += 2;
                    value = new Value(1m / d, next, true, true);
                }
            }

            if (value == null) return null;

            var after = value.Next;
            if (IsWord(tokens, after, "and") && !value.Article)
            {
                var part = after + 1;
                if (IsWord(tokens, part, "a", "an") && part + 1 < tokens.Count && Denominators.TryGetValue(tokens[part + 1].Text, out var d))
                    return value with { Amount = value.Amount + 1m / d, Next = part + 2 };
                if (part < tokens.Count && tokens[part].Kind == Kind.Number && tokens[part].Value < 1m && tokens[part].Value > 0m)
                    return value with { Amount = value.Amount + tokens[part].Value, Next = part + 1 };
            }

            return value;
        }

        private sealed record UnitRead(UnitInfo Info, int Next);

        private static UnitRead? ReadUnit(List<Token> tokens, int i, Token? number, bool article)
        {
            var j = i;
            if (IsSymbol(tokens, j, "-")) j++;
            if (j < tokens.Count && Qualifiers.Contains(tokens[j].Text) && tokens[j].Kind == Kind.Word && j + 1 < tokens.Count && Units.ContainsKey(tokens[j + 1].Text)) j++;
            if (j >= tokens.Count) return null;
            var t = tokens[j];

            if (t.Kind == Kind.Symbol && t.Text is "°" or "º")
            {
                var scale = j + 1;
                if (IsWord(tokens, scale, "F", "Fahrenheit")) return new UnitRead(new UnitInfo(Temperature, "F", 1m), scale + 1);
                if (IsWord(tokens, scale, "C", "Celsius", "centigrade")) return new UnitRead(new UnitInfo(Temperature, "C", 1m), scale + 1);
                return new UnitRead(new UnitInfo(Temperature, "degree", 1m), j + 1);
            }

            if (t.Kind != Kind.Word) return null;
            if (IsWord(tokens, j, "degrees", "degree", "deg"))
            {
                var scale = j + 1;
                if (IsWord(tokens, scale, "F", "Fahrenheit")) return new UnitRead(new UnitInfo(Temperature, "F", 1m), scale + 1);
                if (IsWord(tokens, scale, "C", "Celsius", "centigrade")) return new UnitRead(new UnitInfo(Temperature, "C", 1m), scale + 1);
                return new UnitRead(new UnitInfo(Temperature, "degree", 1m), j + 1);
            }

            var glued = number != null && t.Start == number.End;
            if (glued && t.Text is "F" or "C") return new UnitRead(new UnitInfo(Temperature, t.Text, 1m), j + 1);

            if (IsWord(tokens, j, "fluid", "fl") && IsWord(tokens, j + 1, "ounces", "ounce", "oz"))
                return new UnitRead(new UnitInfo(Volume, "fluid-ounce", 6m), j + 2);

            if (!Units.TryGetValue(t.Text, out var info)) return null;
            if (t.Text is "g" or "gr" && !glued) return null;
            if (info.Unit == "second" && article) return null;
            return new UnitRead(info, j + 1);
        }

        private static UnitRead? ReadInch(List<Token> tokens, int i, string text)
        {
            var j = IsSymbol(tokens, i, "-") ? i + 1 : i;
            return IsWord(tokens, j, "in") && tokens[j].End < text.Length && text[tokens[j].End] == '.'
                ? new UnitRead(new UnitInfo(Length, "inch", 1m), j + 1)
                : null;
        }

        private static UnitRead? UnitAfter(List<Token> tokens, Value value, int startToken, string text) =>
            ReadUnit(tokens, value.Next, tokens[startToken].Kind == Kind.Number ? tokens[value.Next - 1] : null, value.Article)
            ?? ReadInch(tokens, value.Next, text);

        private static int ReadAt(List<Token> tokens, int i, string text, List<ProseQuantity> found)
        {
            var first = ReadValue(tokens, i);
            if (first == null) return i + 1;
            var unit = UnitAfter(tokens, first, i, text);

            if (first.Article && unit == null) return i + 1;

            var low = first.Amount;
            var high = first.Amount;
            var next = unit?.Next ?? first.Next;
            var dimensions = new List<decimal>();

            var connector = next;
            var between = i > 0 && IsWord(tokens, i - 1, "between");
            if (IsSymbol(tokens, connector, "-", "–", "—") || IsWord(tokens, connector, "to", "or") || (between && IsWord(tokens, connector, "and")))
            {
                var second = ReadValue(tokens, connector + 1, allowArticle: false);
                if (second != null)
                {
                    var secondUnit = UnitAfter(tokens, second, connector + 1, text);
                    if (unit == null && secondUnit != null)
                    {
                        unit = secondUnit;
                        high = second.Amount;
                        next = secondUnit.Next;
                    }
                    else if (unit != null && (secondUnit == null || secondUnit.Info.Unit == unit.Info.Unit))
                    {
                        high = second.Amount;
                        next = secondUnit?.Next ?? second.Next;
                    }
                    else if (unit == null && secondUnit == null && !second.Article)
                    {
                        high = second.Amount;
                        next = second.Next;
                    }
                }
            }
            else if (IsWord(tokens, connector, "x", "by") || IsSymbol(tokens, connector, "×"))
            {
                var j = connector;
                var values = new List<decimal> { first.Amount };
                UnitRead? sideUnit = null;
                while (IsWord(tokens, j, "x", "by") || IsSymbol(tokens, j, "×"))
                {
                    var j0 = j + 1;
                    var side = ReadValue(tokens, j0, allowArticle: false);
                    if (side == null || side.Spelled) break;
                    values.Add(side.Amount);
                    j = side.Next;
                    sideUnit = UnitAfter(tokens, side, j0, text);
                    if (sideUnit != null)
                    {
                        j = sideUnit.Next;
                        break;
                    }
                }

                if (values.Count > 1 && (unit ?? sideUnit) is { Info.Family: Length } lengthUnit)
                {
                    foreach (var v in values)
                        found.Add(new ProseQuantity(v, v, Length, lengthUnit.Info.Unit, tokens[i].Start, tokens[j - 1].End, text[tokens[i].Start..tokens[j - 1].End], tokens[i].Bracketed, false));
                    return j;
                }
            }

            if (unit != null && IsWord(tokens, next, "and") && IsWord(tokens, next + 1, "a", "an") && next + 2 < tokens.Count && Denominators.TryGetValue(tokens[next + 2].Text, out var d) && low == high)
            {
                low += 1m / d;
                high = low;
                next += 3;
            }

            var family = unit?.Info.Family ?? Count;
            var unitName = unit?.Info.Unit ?? string.Empty;
            var start = tokens[i].Start;
            var end = tokens[Math.Max(i, next - 1)].End;
            var soft = family == Count && (low == high && low == 1m || (first.Spelled && next < tokens.Count && SoftContext.Contains(tokens[next].Text)));
            if (family == Count && unit == null && first.Spelled && low == high && low == 1m) return next;
            found.Add(new ProseQuantity(low, high, family, unitName, start, end, text[start..end], tokens[i].Bracketed, soft));
            return next;
        }

        private static List<ProseQuantity> Conversions(List<ProseQuantity> quantities, string text)
        {
            for (var k = 0; k < quantities.Count; k++)
            {
                var q = quantities[k];
                if (!q.Conversion) continue;
                var before = k > 0 ? quantities[k - 1] : null;
                var gap = before == null || before.End > q.Start ? null : Regex.Replace(text[before.End..q.Start], @"[\s.]", string.Empty);
                if (before == null || before.Family != q.Family || gap is not ("(" or "/" or "(or")) quantities[k] = q with { Conversion = false };
            }

            return quantities;
        }

        private static List<ProseQuantity> Merge(List<ProseQuantity> found, string text)
        {
            var merged = new List<ProseQuantity>();
            foreach (var q in found)
            {
                if (merged.Count > 0)
                {
                    var last = merged[^1];
                    var gap = last.End <= q.Start ? text[last.End..q.Start].Trim().Trim(',').Trim() : "-";
                    if (last.Family == q.Family && last.Family is Time or Mass or Length && !last.IsRange && !q.IsRange && last.Unit != q.Unit
                        && (gap.Length == 0 || gap.Equals("and", StringComparison.OrdinalIgnoreCase)) && FactorOf(last.Unit) > FactorOf(q.Unit))
                    {
                        var total = (Base(last.Low, last) + Base(q.Low, q)) / FactorOf(q.Unit);
                        merged[^1] = new ProseQuantity(total, total, q.Family, q.Unit, last.Start, q.End, text[last.Start..q.End], last.Conversion, false);
                        continue;
                    }
                }

                merged.Add(q);
            }

            return merged;
        }
    }
}

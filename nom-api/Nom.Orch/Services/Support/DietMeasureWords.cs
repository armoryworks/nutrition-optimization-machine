using System.Text.RegularExpressions;

namespace Nom.Orch.Services.Support
{
    /// <summary>
    /// The measures named after a drink or a food ("wineglassful", "wine glass", "teacup", "coffee cup",
    /// "saltspoon"), each read as one whole word: an amount, never the thing it is named
    /// after (staging checkpoint 4, P5: the aliases "wine glassful of salt" and "Wineglass Water" put
    /// Salt and Water under Sulfite Sensitivity's <c>%wine%</c>). The diet gate blanks them out of an
    /// alias before a pattern reads it, and the alias re-check gives no rule to an alias that holds one.
    /// </summary>
    public static class DietMeasureWords
    {
        /// <summary>The version of the measure words, part of every cache key that holds a reading of an alias.</summary>
        public const string Version = "diet-measures/2";

        private const string Body = @"(?:(?:wine|claret|sherry|port|liqueur|cordial|whiske?y|brandy|champagne|beer)[- ]?(?:glass|spoon)|(?:tea|coffee|breakfast)[- ]?(?:glass|cup|spoon)|salt[- ]?spoon)(?:e?s)?(?:[- ]?full?s?)?";

        /// <summary>A measure named after a drink or a food, as one word or two (spaced or hyphenated), in the singular or plural, with or without "ful": a drink's glass or spoon, a tea, coffee or breakfast glass, cup or spoon, or a saltspoon; never a drink's cup ("claret cup", "champagne cup", "wine cup" are punches, review CK9S-3).</summary>
        public static readonly Regex Pattern = new(@"(?<![\p{L}\p{N}])" + Body + @"(?![\p{L}\p{N}])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        /// <summary>The same measures as a PostgreSQL expression, for <c>regexp_replace(..., 'gi')</c> in <c>ops/diet-gate-count.sql</c>.</summary>
        public const string PostgresPattern = @"\m" + Body + @"\M";

        /// <summary>Whether the text holds such a measure.</summary>
        public static bool Holds(string? text) => !string.IsNullOrEmpty(text) && Pattern.IsMatch(text);

        /// <summary>The text with every such measure blanked to a space.</summary>
        public static string Blank(string text) => string.IsNullOrEmpty(text) ? text : Pattern.Replace(text, " ");
    }
}

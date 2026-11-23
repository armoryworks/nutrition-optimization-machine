using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Nom.Orch.Services.Support
{
    /// <summary>
    /// The container kinds a size is kept for, and how a recipe line names one: the first
    /// container word of the line ("1 can tomatoes", "2 pkgs. cream cheese", "1 jar marinara"),
    /// the size word right before it ("1 large can"), and whether the line weighs the food drained.
    /// </summary>
    public static class ContainerKinds
    {
        public const string Can = "can";
        public const string Jar = "jar";
        public const string Bottle = "bottle";
        public const string Box = "box";
        public const string Bag = "bag";
        public const string Packet = "packet";
        public const string Envelope = "envelope";
        public const string Carton = "carton";
        public const string Tub = "tub";
        public const string Package = "package";

        public const string Small = "small";
        public const string Large = "large";

        public static readonly IReadOnlyList<string> All = new[] { Can, Jar, Bottle, Box, Bag, Packet, Envelope, Carton, Tub, Package };

        private static readonly (Regex Pattern, string Kind)[] Words =
        {
            (new Regex(@"^(cans?|tins?)$", RegexOptions.IgnoreCase), Can),
            (new Regex(@"^jars?$", RegexOptions.IgnoreCase), Jar),
            (new Regex(@"^bottles?$", RegexOptions.IgnoreCase), Bottle),
            (new Regex(@"^box(es)?$", RegexOptions.IgnoreCase), Box),
            (new Regex(@"^bags?$", RegexOptions.IgnoreCase), Bag),
            (new Regex(@"^(packets?|pouch(es)?|sachets?)$", RegexOptions.IgnoreCase), Packet),
            (new Regex(@"^envelopes?$", RegexOptions.IgnoreCase), Envelope),
            (new Regex(@"^cartons?$", RegexOptions.IgnoreCase), Carton),
            (new Regex(@"^(tubs?|containers?)$", RegexOptions.IgnoreCase), Tub),
            (new Regex(@"^(packages?|pkgs?|packs?)$", RegexOptions.IgnoreCase), Package),
        };

        private static readonly Regex Word = new(@"[A-Za-z]+", RegexOptions.Compiled);

        private static readonly Regex SizeBefore = new(
            @"\b(?<size>small|large|big|large-size|small-size)\s+(?:size\s+)?(?<word>cans?|tins?|jars?|bottles?|box(?:es)?|bags?|packets?|pouch(?:es)?|envelopes?|cartons?|tubs?|containers?|packages?|pkgs?|packs?)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex Drained = new(@"\bdrain(ed|ing)?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex ItemSizePhrase = new(
            @"\b(?:street[\s-]+taco|soft[\s-]+taco|taco|fajita|burrito|snack|fun|bite|family|party|king|jumbo|cocktail|slider|club|individual|single[\s-]+serve)[\s-]+siz(?:e|ed)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex NotDrained = new(
            @"\b(do\s+not\s+drain|don't\s+drain|undrained|not\s+drained|with\s+(?:its|their|the)?\s*(?:liquid|juices?|syrup|liquor)|and\s+(?:its|their)\s+(?:liquid|juices?)|reserve\s+(?:the\s+)?(?:liquid|juices?))\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>The kind the line's first container word names, or null when it names none.</summary>
        public static string? FromText(string? rawLine)
        {
            if (string.IsNullOrWhiteSpace(rawLine)) return null;
            foreach (Match m in Word.Matches(rawLine))
            {
                foreach (var (pattern, kind) in Words)
                {
                    if (pattern.IsMatch(m.Value)) return kind;
                }
            }
            return null;
        }

        /// <summary>small or large when the word stands right before the container word, else empty.</summary>
        public static string SizeWord(string? rawLine)
        {
            if (string.IsNullOrWhiteSpace(rawLine)) return string.Empty;
            var m = SizeBefore.Match(rawLine);
            if (!m.Success) return string.Empty;
            return m.Groups["size"].Value.StartsWith("small", StringComparison.OrdinalIgnoreCase) ? Small : Large;
        }

        /// <summary>
        /// The size of the items or of the pack the line names ("street taco size", "snack size",
        /// "family size"), which no container size of the food speaks of: a 484 g package of 49 g
        /// tortillas is not a package of street-taco tortillas (staging checkpoint 2, 35714). Null when
        /// it names none.
        /// </summary>
        public static string? ItemSize(string? rawLine)
        {
            if (string.IsNullOrWhiteSpace(rawLine)) return null;
            var m = ItemSizePhrase.Match(rawLine);
            return m.Success ? m.Value.ToLowerInvariant() : null;
        }

        /// <summary>Whether the line weighs the food drained ("drained", "drained and rinsed"), and does not keep its liquid.</summary>
        public static bool SaysDrained(string? rawLine) =>
            !string.IsNullOrWhiteSpace(rawLine) && Drained.IsMatch(rawLine) && !NotDrained.IsMatch(rawLine);

        /// <summary>Whether a USDA description names the drained solids of a packed food.</summary>
        public static bool DescribesDrained(string? description) =>
            !string.IsNullOrWhiteSpace(description) && Regex.IsMatch(description, @"\bdrained\b", RegexOptions.IgnoreCase);
    }

    /// <summary>The eras a container size is kept for, and the era of a recipe's source.</summary>
    public static class ContainerEras
    {
        public const string Modern = "modern";
        public const string Historic = "historic";
        public const string Any = "any";
        public const string Unknown = "unknown";

        /// <summary>The year from which a book is modern.</summary>
        public const int ModernFrom = 1960;

        public static string FromYear(int? year) => year is null ? Unknown : year < ModernFrom ? Historic : Modern;

        /// <summary>The hosts of the book archives a book recipe's source URL may point to; any other host is a web page.</summary>
        public static readonly IReadOnlyList<string> BookArchives = new[] { "gutenberg.org", "archive.org", "loc.gov", "hathitrust.org" };

        /// <summary>
        /// The era of a recipe: modern for a web recipe and for one with no source at all (written in
        /// the app); for a book, the era of its row in recipe.RecipeSourceEra, else modern when its
        /// source URL is a web page outside the book archives (USDA MyPlate Kitchen: staging checkpoint
        /// 3, N13), else unknown.
        /// </summary>
        public static string ForRecipe(string? sourceSite, string? sourceAttribution, IReadOnlyDictionary<string, string> eras, string? sourceUrl = null)
        {
            if (!string.IsNullOrWhiteSpace(sourceSite) || string.IsNullOrWhiteSpace(sourceAttribution)) return Modern;
            if (eras.TryGetValue(sourceAttribution, out var era) && era is Modern or Historic) return era;
            return IsWebPage(sourceUrl) ? Modern : Unknown;
        }

        /// <summary>Whether a source URL (or its bare host) is a web page outside the book archives; false for none.</summary>
        public static bool IsWebPage(string? sourceUrlOrHost)
        {
            if (string.IsNullOrWhiteSpace(sourceUrlOrHost) || sourceUrlOrHost.Trim() == "-") return false;
            var text = sourceUrlOrHost.Trim();
            var host = text.Contains("://", StringComparison.Ordinal) ? (Uri.TryCreate(text, UriKind.Absolute, out var uri) ? uri.Host : string.Empty) : text;
            host = host.ToLowerInvariant();
            return host.Length > 0 && !BookArchives.Any(a => host == a || host.EndsWith("." + a, StringComparison.Ordinal));
        }
    }

    /// <summary>The states of a container size row.</summary>
    public static class ContainerSizeStatus
    {
        public const string Active = "active";
        public const string Shadow = "shadow";
        public const string Paused = "paused";
        public const string Retired = "retired";
    }

    /// <summary>The words of a description as the class and product match reads them: lower case, simple plurals made singular.</summary>
    public static class ContainerWords
    {
        private static readonly Regex Token = new(@"[a-z0-9%]+", RegexOptions.Compiled);

        public static HashSet<string> Of(string? text) =>
            Token.Matches((text ?? string.Empty).ToLowerInvariant()).Select(m => Singular(m.Value)).ToHashSet(StringComparer.Ordinal);

        public static string Singular(string word)
        {
            if (word.Length > 4 && word.EndsWith("ies", StringComparison.Ordinal)) return word[..^3] + "y";
            if (word.Length > 4 && word.EndsWith("oes", StringComparison.Ordinal)) return word[..^2];
            if (word.Length > 3 && word.EndsWith('s') && !word.EndsWith("ss", StringComparison.Ordinal)) return word[..^1];
            return word;
        }

        /// <summary>Every comma-separated item is present (an item may give alternatives with |); "-" or empty matches anything.</summary>
        public static bool HasAll(string spec, ISet<string> words) =>
            spec is "" or "-" || spec.Split(',').All(item => item.Split('|').Any(alt => words.Contains(Singular(alt.Trim().ToLowerInvariant()))));

        /// <summary>Any comma-separated word is present; "-" or empty matches nothing.</summary>
        public static bool HasAny(string spec, ISet<string> words) =>
            spec is not ("" or "-") && spec.Split(',').Any(w => words.Contains(Singular(w.Trim().ToLowerInvariant())));
    }

    /// <summary>The most common size of a class of branded products, and why there is none.</summary>
    public sealed record ContainerMode(int Grams, int Products, int Band, int RunnerUp, string? Refusal)
    {
        public bool Passes => Refusal == null;

        public double Share => Products == 0 ? 0 : (double)Band / Products;
    }

    /// <summary>
    /// "Assume the most common size": the mode of the parsed package sizes (one per product), with
    /// the bands of ±5 % around each size. A mode counts only when at least
    /// <see cref="MinimumProducts"/> products have a size, its band holds at least half of them,
    /// and its band is at least twice the next band. A size word looks only at the sizes below
    /// ("small") or above ("large") the overall mode's band, with the same three tests.
    /// </summary>
    public static class ContainerModeRule
    {
        public const int MinimumProducts = 20;
        public const double BandWidth = 0.05;
        public const double MinimumShare = 0.5;
        public const int RunnerUpFactor = 2;

        public const string FewProducts = "few products";
        public const string NoClearMode = "no clear mode";
        public const string RunnerUpClose = "runner-up too close";

        public static ContainerMode? Decide(IReadOnlyDictionary<int, int> sizes, string sizeWord = "", int minimum = MinimumProducts)
        {
            var all = sizes.Where(s => s.Key > 0 && s.Value > 0).ToDictionary(s => s.Key, s => s.Value);
            if (all.Count == 0) return null;
            if (sizeWord is ContainerKinds.Small or ContainerKinds.Large)
            {
                var overall = ModeOf(all);
                all = all.Where(s => sizeWord == ContainerKinds.Small ? s.Key < overall * (1 - BandWidth) : s.Key > overall * (1 + BandWidth))
                    .ToDictionary(s => s.Key, s => s.Value);
                if (all.Count == 0) return null;
            }

            var total = all.Values.Sum();
            var mode = ModeOf(all);
            var band = InBand(all, mode);
            var rest = all.Where(s => !Within(s.Key, mode)).ToDictionary(s => s.Key, s => s.Value);
            var runnerUp = rest.Count == 0 ? 0 : InBand(rest, ModeOf(rest));
            var refusal = total < minimum ? FewProducts
                : band < MinimumShare * total ? NoClearMode
                : band < RunnerUpFactor * runnerUp ? RunnerUpClose
                : null;
            return new ContainerMode(mode, total, band, runnerUp, refusal);
        }

        private static int ModeOf(Dictionary<int, int> sizes) =>
            sizes.OrderByDescending(s => s.Value).ThenBy(s => s.Key).First().Key;

        private static int InBand(Dictionary<int, int> sizes, int mode) => sizes.Where(s => Within(s.Key, mode)).Sum(s => s.Value);

        private static bool Within(int size, int mode) => Math.Abs(size - mode) <= BandWidth * mode;

        /// <summary>Parses a frozen size list ("425:120;439:30").</summary>
        public static Dictionary<int, int> ParseSizes(string? text)
        {
            var sizes = new Dictionary<int, int>();
            if (string.IsNullOrWhiteSpace(text) || text == "-") return sizes;
            foreach (var part in text.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var bits = part.Split(':');
                sizes[int.Parse(bits[0], CultureInfo.InvariantCulture)] = int.Parse(bits[1], CultureInfo.InvariantCulture);
            }
            return sizes;
        }
    }

    /// <summary>Two sizes for one key agree when they lie within 10 % of each other.</summary>
    public static class ContainerSizeAgreement
    {
        public const decimal Tolerance = 0.10m;

        public static bool Agree(decimal a, decimal b) => a > 0 && b > 0 && Math.Abs(a - b) <= Tolerance * Math.Max(a, b);

        /// <summary>A hold-out hit: the assumed size lies within 10 % of the size the line states.</summary>
        public static bool Hit(decimal assumed, decimal stated) => stated > 0 && Math.Abs(assumed - stated) <= Tolerance * stated;
    }

    /// <summary>One stated size the hold-out compared with the size the lane would assume.</summary>
    public sealed record ContainerHoldoutLine(bool Hit, DateTime At);

    /// <summary>A verdict on one container size key: its status and why, in figures.</summary>
    public sealed record ContainerKeyVerdict(string Status, string Reason, int Lines, int Agree);

    /// <summary>
    /// When a container size applies by itself, stays in shadow or pauses itself, from the
    /// hold-out of stated sizes (docs/architecture/food-catalog-ingestion.md, "Container sizes"):
    /// a key with at least <see cref="KeyLines"/> hold-out lines applies when its agreement is at
    /// least 95 % and its Wilson lower bound at least 90 % (with fewer lines than that bound can
    /// reach, 35, a key at 95 % also applies when its class qualifies); a key with fewer applies when its class
    /// agrees on at least 95 % of at least <see cref="ClassLines"/> lines and its own source passed
    /// its test. An active key pauses when the last 200 lines of its class agree under 90 % or its
    /// own last 20 hold 3 misses, and a shadow key that meets either condition stays in shadow. A
    /// paused key comes back only when its size or source changes.
    /// </summary>
    public static class ContainerSizeQualifier
    {
        public const int KeyLines = 20;
        public const int ClassLines = 50;
        public const double Share = 0.95;
        public const double Lower = 0.90;
        public const int RollingWindow = 200;
        public const double RollingShare = 0.90;
        public const int RecentWindow = 20;
        public const int RecentMisses = 3;

        public static ContainerKeyVerdict Decide(string currentStatus, IReadOnlyList<ContainerHoldoutLine> key, IReadOnlyList<ContainerHoldoutLine> cls, bool sourcePasses)
        {
            var keyAgree = key.Count(l => l.Hit);
            if (currentStatus == ContainerSizeStatus.Paused)
                return new(ContainerSizeStatus.Paused, "paused; comes back only after its size or source changes and a fresh score passes", key.Count, keyAgree);

            var ordered = cls.OrderBy(l => l.At).ToList();
            var rolling = ordered.Skip(Math.Max(0, ordered.Count - RollingWindow)).ToList();
            var recent = key.OrderBy(l => l.At).Skip(Math.Max(0, key.Count - RecentWindow)).ToList();
            var held = currentStatus == ContainerSizeStatus.Active ? ContainerSizeStatus.Paused : ContainerSizeStatus.Shadow;
            if (rolling.Count >= RollingWindow && (double)rolling.Count(l => l.Hit) / rolling.Count < RollingShare)
                return new(held, Fmt($"its class agreed on {rolling.Count(l => l.Hit)} of its last {rolling.Count} hold-out lines, under {RollingShare:P0}"), key.Count, keyAgree);
            if (recent.Count(l => !l.Hit) >= RecentMisses)
                return new(held, Fmt($"{recent.Count(l => !l.Hit)} misses in its last {recent.Count} hold-out lines"), key.Count, keyAgree);

            var classAgree = cls.Count(l => l.Hit);
            var classShare = cls.Count == 0 ? 0 : (double)classAgree / cls.Count;
            var classQualifies = cls.Count >= ClassLines && classShare >= Share;
            if (key.Count >= KeyLines)
            {
                var share = (double)keyAgree / key.Count;
                var lower = LaneQualifier.Wilson(keyAgree, key.Count).Lower;
                var tooFewForBound = key.Count < LaneQualifier.MinimumSample(Lower);
                if (share >= Share && lower >= Lower)
                    return new(ContainerSizeStatus.Active, Fmt($"agrees on {keyAgree} of {key.Count} hold-out lines ({share:P1}, lower bound {lower:P1})"), key.Count, keyAgree);
                if (share >= Share && tooFewForBound && classQualifies && sourcePasses)
                    return new(ContainerSizeStatus.Active, Fmt($"agrees on {keyAgree} of {key.Count} hold-out lines ({share:P1}), too few for a {Lower:P0} bound; its class agrees on {classAgree} of {cls.Count} ({classShare:P1})"), key.Count, keyAgree);
                return new(ContainerSizeStatus.Shadow, Fmt($"agrees on {keyAgree} of {key.Count} hold-out lines ({share:P1}, lower bound {lower:P1}); needs {Share:P0} and {Lower:P0}"), key.Count, keyAgree);
            }

            if (!sourcePasses)
                return new(ContainerSizeStatus.Shadow, Fmt($"{key.Count} hold-out lines of its own and its source did not pass its own test"), key.Count, keyAgree);
            return classQualifies
                ? new(ContainerSizeStatus.Active, Fmt($"{key.Count} hold-out lines of its own; its class agrees on {classAgree} of {cls.Count} ({classShare:P1})"), key.Count, keyAgree)
                : new(ContainerSizeStatus.Shadow, Fmt($"{key.Count} hold-out lines of its own; its class agrees on {classAgree} of {cls.Count} ({classShare:P1}), needs {Share:P0} on {ClassLines}"), key.Count, keyAgree);
        }

        private static string Fmt(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
    }
}

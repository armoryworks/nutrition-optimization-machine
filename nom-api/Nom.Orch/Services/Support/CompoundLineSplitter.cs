using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;

namespace Nom.Orch.Services.Support
{
    /// <summary>What a compound line becomes: rows on different foods, a move to its first alternative, or nothing.</summary>
    public enum CompoundKind
    {
        Split,
        PickAlternative,
        Leave,
    }

    /// <summary>One food a compound line names, with the line its row keeps and the amount the line parser reads from it (null for none).</summary>
    public sealed record CompoundPart(string Name, long FoodId, string RawLine, ParsedQuantity? Quantity);

    /// <summary>
    /// The decision for one compound line. A split lists one part per food, a pick lists the
    /// chosen alternative alone, and a line left alone carries its <see cref="LeaveReason"/>.
    /// </summary>
    public sealed record CompoundPlan(string? Rule, CompoundKind Kind, IReadOnlyList<CompoundPart> Parts, string Wording, string? LeaveReason)
    {
        public static CompoundPlan Leave(string wording, string reason) => new(null, CompoundKind.Leave, Array.Empty<CompoundPart>(), wording, reason);
    }

    /// <summary>
    /// Reads a recipe line that names more than one food ("salt and pepper to taste", "1 cup each
    /// flour and sugar", "butter or margarine") and decides, by fixed rules, which foods it names
    /// and what each food's own line says. A part line is only ever the source line with words
    /// deleted, and a split is planned only when every part line reads back through the line
    /// parser and the name rules as exactly the row it becomes. Pure text rules, no model.
    /// </summary>
    public static class CompoundLineSplitter
    {
        public const string AndSaltPepper = "and-salt-pepper";
        public const string AndUnquantified = "and-unquantified";
        public const string AndEach = "and-each";
        public const string OwnQuantity = "own-quantity";
        public const string PartPair = "part-pair";
        public const string AndQuantifiedSaltPepper = "and-quantified-salt-pepper";
        public const string OrIndependent = "or-independent";
        public const string OrSharedHead = "or-shared-head";
        public const string OrFirstNamed = "or-first-named";

        public static readonly IReadOnlyList<string> Rules = new[]
        {
            AndSaltPepper, AndUnquantified, AndEach, OwnQuantity, PartPair, AndQuantifiedSaltPepper, OrIndependent, OrSharedHead, OrFirstNamed,
        };

        public const string LeaveFolded = "folded";
        public const string LeaveProtected = "protected name";
        public const string LeavePreparation = "preparation words";
        public const string LeaveOneFood = "one food";
        public const string LeaveNotInCatalog = "and-list part not in catalog";
        public const string LeaveSharedHead = "and-list with a shared head";
        public const string LeaveAndWithOr = "and with or";
        public const string LeaveTooManyNames = "more than four names";
        public const string LeaveEachWithoutList = "each without a usable list";
        public const string LeaveQuantifiedList = "quantified and-list";
        public const string LeaveOrAmbiguous = "or-ambiguous";
        public const string LeaveOrLastNamed = "or-last-named";
        public const string LeaveOrUnresolved = "or-unresolved";
        public const string LeaveFlavour = "bare flavour alternative";
        public const string LeaveUnlocated = "names not found in the line";
        public const string LeaveNotFixedPoint = "part line does not read back";
        public const string LeaveBareNumber = "bare number on a food not counted in pieces";
        public const string LeaveEachUnread = "each with an amount that does not read";
        public const string LeaveCutPepper = "cut pepper is a vegetable, not the spice";
        public const string LeaveSpiceMustard = "mustard beside salt, pepper, a spice or sugar means dry mustard";
        public const string LeaveWholeFoodProduct = "alternatives of a whole food and a product of another";
        public const string LeaveSharedProduct = "whole foods sharing a product the catalog does not hold";

        private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;

        private const string NumberWord = @"(?:\d+(?:[.,/]\d+)?|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|a\s+few|several)";

        private static readonly Regex Range = new($@"\b{NumberWord}\s+(?:or|to|and)\s+(?:{NumberWord}|more)\b", Options);

        private static readonly Regex Conjunction = new(@"(?<![\p{L}])(?:and|or|plus)(?![\p{L}])|&", Options);

        private static readonly Regex Separator = new(
            @"\s*,\s*(?:(?<c>and|or|plus|&)\s+)?|\s+(?<c>and|or|plus)\s+|\s*(?<c>&)\s*", Options);

        private static readonly Regex Each = new(@"\beach\s+", Options);

        private static readonly Regex ListInPrefix = new(@",|(?<![\p{L}])(?:and|or|plus)(?![\p{L}])|&", Options);

        private static readonly Regex Whitespace = new(@"\s+", RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex LeadingNumber = new(@"^\s*[\d¼½¾⅓⅔⅛⅜⅝⅞]", RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex OwnSeparator = new(@"\s*,?\s+(?:and|plus)\s+|\s*&\s*|\s*,\s+(?=[\d¼½¾⅓⅔⅛⅜⅝⅞])", Options);

        private const string Fruit = @"(?<fruit>lemon|lime|orange|grapefruit)s?";
        private const string Juice = @"juice";
        private const string Rind = @"zest|rind|peel";

        private static readonly Regex PartPairOf = new(
            $@"\b(?<adj>(?:finely\s+)?grated\s+)?(?<p1>{Juice}|{Rind})\s+and\s+(?<g>(?:grated\s+|the\s+)*)(?<p2>{Juice}|{Rind})\s+of\s+.*?\b{Fruit}\b", Options);

        private static readonly Regex PartPairAfter = new(
            $@"\b{Fruit}\b\s*,?\s*(?:(?:the|its)\s+)?(?<p1>{Juice}|{Rind})\s+and\s+(?<g>(?:grated\s+|the\s+)*)(?<p2>{Juice}|{Rind})\b", Options);

        private static readonly HashSet<string> Prep = new(StringComparer.OrdinalIgnoreCase)
        {
            "chopped", "minced", "pressed", "grated", "drained", "rinsed", "melted", "cubed", "sliced", "diced", "peeled", "cored", "seeded",
            "softened", "beaten", "crushed", "divided", "cooled", "cold", "slightly", "finely", "coarsely", "roughly", "thinly", "freshly",
            "well", "cut", "into", "in", "pieces", "piece", "halved", "quartered", "trimmed", "washed", "julienned", "shredded", "mashed",
            "cooked", "toasted", "room", "temperature", "at", "deveined", "pitted", "stemmed", "cleaned", "warmed", "warm", "hot", "heated",
            "separated", "lightly", "packed", "sifted", "squeezed", "zested", "juiced", "torn", "crumbled", "smashed", "thawed", "scrubbed",
            "boned", "skinned", "patted", "dry", "dried", "very", "small", "large", "medium", "chunks", "cubes", "dice", "inch", "thin",
            "thick", "strips", "rings", "wedges", "flaked", "sorted", "soaked", "picked", "over", "through", "removed", "discarded", "reserved",
            "chilled", "frozen", "fresh", "ripe", "raw", "blanched", "roasted", "steamed", "boiled", "the", "a", "to", "taste", "more", "plus",
            "for", "serving", "garnish", "optional", "if", "desired", "needed", "as", "about", "up", "out", "off", "of", "and", "or", "ground",
        };

        private static readonly char[] WordBreaks = { ' ', '-' };

        private static readonly string[] PeelNames = { "zest", "peel", "rind" };

        private static readonly HashSet<string> Flavours = new(StringComparer.OrdinalIgnoreCase)
        {
            "vanilla", "almond", "rose", "peppermint", "wintergreen", "pistachio",
        };

        private static readonly Regex FlavourWord = new(@"\b(?:extract|essence|flavou?r(?:ing)?s?)\b", Options);

        private static readonly HashSet<string> FlavourFruits = new(StringComparer.OrdinalIgnoreCase) { "lemon", "orange", "lime" };

        private static readonly HashSet<string> Spices = new(StringComparer.OrdinalIgnoreCase)
        {
            "nutmeg", "cinnamon", "mace", "allspice", "cloves", "clove", "ginger", "vanilla", "almond", "cardamom", "coriander",
        };

        private static readonly HashSet<string> MustardCompanions = new(StringComparer.OrdinalIgnoreCase)
        {
            "cayenne", "paprika", "sugar", "turmeric", "curry",
        };

        private static readonly Regex AnyHead = new(@"^any\s+(?!other\b)(?<head>\p{L}+)$", Options);

        private static readonly Regex AnyKind = new(@"^any\s+(?!other\b)", Options);

        private static readonly Regex AnyOther = new(@"^(?:any\s+)?other\s+(?=\p{L})", Options);

        private static readonly HashSet<string> ProductWords = new(StringComparer.OrdinalIgnoreCase)
        {
            "jam", "jams", "marmalade", "marmalades", "preserves", "jelly", "jellies", "juice", "juices", "brandy", "sauce", "butter", "oil",
        };

        private static readonly Regex CutWord = new(@"\b(?:chopped|minced|diced|sliced|seeded|shredded|cut|strips?|rings?)\b", Options);

        private static readonly Regex CountStart = new(
            @"^\s*(?:[\d¼½¾⅓⅔⅛⅜⅝⅞]|(?:one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|dozen)\b)", Options);

        private static readonly Regex Purpose = new(@"\s+(?:to|for)\s+\S.*$", Options);

        private static readonly Regex HalfStart = new(@"^\s*(?:half|quarter|a\s+half|one[- ]half)\b", Options);

        private static readonly Regex MeasureWord = new(
            @"\b(?:(?:tea|table|dessert|salt|coffee)[- ]?)?spoon|\bcup|\bglass|\bgill|\bpinch|\bdash|\bounce|\boz\b|\bpound|\blbs?\b|\bpint|\bquart|\bdrachm|\bscruple|\bgrain", Options);

        private static readonly Regex ListMeasure = new(
            @"(?:^|[\s(])(?:[\d¼½¾⅓⅔⅛⅜⅝⅞][\d¼½¾⅓⅔⅛⅜⅝⅞/.\-–]*|an?|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|half|quarter)\s+(?:(?:heaped|heaping|level|rounded|large|small|good|scant|full|generous|big)\s+)?(?:(?:tea|table|dessert|salt|coffee|wine)[- ]?)?(?:spoon(?:ful)?s?|cup(?:ful)?s?|glass(?:ful)?(?:es)?|gills?|pinch(?:es)?|dash(?:es)?|ounces?|oz|pounds?|lbs?|pints?|quarts?|drachms?|scruples?|grains?)\b", Options);

        private static readonly HashSet<string> PartWords = new(StringComparer.OrdinalIgnoreCase)
        {
            "juice", "zest", "rind", "peel", "the", "its", "grated", "fresh", "freshly", "squeezed", "finely", "large", "small", "medium",
            "ripe", "juicy", "of", "a", "an", "half", "whole",
        };

        /// <summary>
        /// The plan for one recipe line, or null when the line does not name more than one food
        /// (no conjunction, a numeric range such as "two or three eggs", or a wording that is
        /// itself a food such as "half and half" or "peas and carrots").
        /// </summary>
        public static CompoundPlan? Plan(string rawLine, CompoundVocabulary v)
        {
            if (string.IsNullOrWhiteSpace(rawLine)) return null;
            if (rawLine.Contains(FoldedLine.Separator, StringComparison.Ordinal))
            {
                return rawLine.Split(FoldedLine.Separator).Select(segment => PlanOne(segment, v)).Any(p => p is { Kind: not CompoundKind.Leave })
                    ? CompoundPlan.Leave(IngredientNameNormalizer.Normalize(rawLine), LeaveFolded)
                    : null;
            }

            return PlanOne(rawLine, v);
        }

        private static CompoundPlan? PlanOne(string rawLine, CompoundVocabulary v)
        {
            var line = Whitespace.Replace(IngredientNameNormalizer.PlainFractions(WebUtility.HtmlDecode(rawLine)), " ").Trim();
            if (line.Length == 0) return null;

            if (PartPairPlan(line, v) is { } pair) return pair;

            var wording = IngredientNameNormalizer.Normalize(line);
            if (v.Resolve(wording) != null) return null;
            var masked = WithoutTrailingPreparation(Range.Replace(wording, "#"));
            if (!Conjunction.IsMatch(masked) || v.Resolve(masked) != null) return null;
            if (v.HoldsProtected(wording)) return CompoundPlan.Leave(wording, LeaveProtected);

            if (OwnQuantityPlan(line, wording, v) is { } own) return own;

            var names = new List<string>();
            var conjunctions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var at = 0;
            foreach (Match separator in Separator.Matches(masked))
            {
                names.Add(masked[at..separator.Index].Trim());
                at = separator.Index + separator.Length;
                if (separator.Groups["c"].Success) conjunctions.Add(separator.Groups["c"].Value is "&" or "plus" ? "and" : separator.Groups["c"].Value.ToLowerInvariant());
            }

            names.Add(masked[at..].Trim());
            if (conjunctions.Count == 0) return null;
            if (conjunctions.Count > 1) return CompoundPlan.Leave(wording, LeaveAndWithOr);
            if (names.Any(n => n.Length == 0 || n.Contains('#'))) return CompoundPlan.Leave(wording, LeaveUnlocated);
            if (names.Count > 4) return CompoundPlan.Leave(wording, LeaveTooManyNames);
            if (names.Any(IsPreparation)) return CompoundPlan.Leave(wording, LeavePreparation);

            return conjunctions.Contains("or") ? Alternatives(line, wording, names, v) : AndList(line, wording, names, v);
        }

        private static string WithoutTrailingPreparation(string wording)
        {
            var comma = wording.LastIndexOf(',');
            while (comma > 0 && IsPreparation(wording[(comma + 1)..]))
            {
                wording = wording[..comma].TrimEnd();
                comma = wording.LastIndexOf(',');
            }

            return wording;
        }

        private static bool IsPreparation(string name)
        {
            var words = name.Split(WordBreaks, StringSplitOptions.RemoveEmptyEntries);
            return words.All(w => Prep.Contains(w.Trim('.', ';', ':')) || w.Any(char.IsDigit) || w.EndsWith("ly", StringComparison.OrdinalIgnoreCase));
        }

        private static string Head(string name) => name.Split(' ', StringSplitOptions.RemoveEmptyEntries)[^1];

        private static bool IsSingleWord(string name) => !name.Contains(' ');

        /// <summary>
        /// Whether the first name reads as a modifier of the last one's head ("milk or dark
        /// chocolate chips", "garlic and onion powder"): the first name, or its last word, joined to
        /// the last one's head, or to every word of the last one after its first, resolves to a
        /// food or is the name of an ingredient, in singular or plural form.
        /// </summary>
        private static bool SharesHead(string first, string last, CompoundVocabulary v)
        {
            var words = last.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length < 2) return false;
            var joined = new[] { first, Head(first) }.SelectMany(f => new[] { $"{f} {words[^1]}", $"{f} {string.Join(' ', words.Skip(1))}" });
            return joined.Distinct(StringComparer.OrdinalIgnoreCase)
                .SelectMany(j => new[] { j, j.EndsWith('s') ? j[..^1] : j + "s", j.EndsWith("es", StringComparison.OrdinalIgnoreCase) ? j[..^2] : j })
                .Any(j => v.Resolve(j) != null || v.IsKnown(j));
        }

        private static CompoundPlan Alternatives(string line, string wording, List<string> names, CompoundVocabulary v)
        {
            if (names.Any(n => Flavours.Contains(n) || FlavourWord.IsMatch(n))) return CompoundPlan.Leave(wording, LeaveFlavour);
            if (names.Any(FlavourFruits.Contains) && names.Any(Spices.Contains)) return CompoundPlan.Leave(wording, LeaveFlavour);

            var first = names[0];
            var last = names[^1];
            if (AnyHead.Match(last) is { Success: true } any)
            {
                if (!names.Take(names.Count - 1).All(IsSingleWord)) return CompoundPlan.Leave(wording, LeaveOrAmbiguous);
                var kind = $"{first} {any.Groups["head"].Value}";
                return v.Resolve(kind) is { } kindFood && kindFood != v.Resolve(first)
                    ? new CompoundPlan(OrSharedHead, CompoundKind.PickAlternative, new[] { new CompoundPart(kind, kindFood, line, null) }, wording, null)
                    : CompoundPlan.Leave(wording, LeaveOrAmbiguous);
            }

            if (AnyKind.IsMatch(last)) return CompoundPlan.Leave(wording, LeaveOrAmbiguous);

            var core = Purpose.Replace(last, string.Empty);
            if (WholeFoodBesideProduct(first, core, v)) return CompoundPlan.Leave(wording, LeaveWholeFoodProduct);

            if (IsSingleWord(first) && last.Split(' ', StringSplitOptions.RemoveEmptyEntries) is { Length: 2 } tail
                && v.Resolve(last) == null && v.Resolve(tail[0]) != null)
            {
                return CompoundPlan.Leave(wording, LeaveOrAmbiguous);
            }

            var foods = names.Select(v.Resolve).ToList();
            var shared = IsSingleWord(core) ? null : $"{first} {Head(core)}";
            var sharedFood = shared == null ? null : v.Resolve(shared);
            var sharedKnown = shared != null && v.IsKnown(shared);

            if (foods[0] is { } firstFood)
            {
                if (SharedProduct(line, wording, names, firstFood, v) is { } product) return product;
                if (sharedFood is { } other && other != firstFood) return CompoundPlan.Leave(wording, LeaveOrAmbiguous);
                if (sharedFood == null && (sharedKnown || SharesHead(first, core, v))) return CompoundPlan.Leave(wording, LeaveOrAmbiguous);
                if (foods.Skip(1).Any(f => f == firstFood)) return CompoundPlan.Leave(wording, LeaveOneFood);
                var rule = foods.All(f => f != null) ? OrIndependent : OrFirstNamed;
                return new CompoundPlan(rule, CompoundKind.PickAlternative, new[] { new CompoundPart(first, firstFood, line, null) }, wording, null);
            }

            if (sharedFood is { } head)
            {
                if (foods[^1] == head) return CompoundPlan.Leave(wording, LeaveOneFood);
                return new CompoundPlan(OrSharedHead, CompoundKind.PickAlternative, new[] { new CompoundPart(shared!, head, line, null) }, wording, null);
            }

            return CompoundPlan.Leave(wording, foods.Skip(1).Any(f => f != null) ? LeaveOrLastNamed : LeaveOrUnresolved);
        }

        /// <summary>
        /// A list of whole foods whose later alternatives end in a product word ("apple, peach, or any
        /// other marmalade", "peach or French brandy", "raspberry, cherry, or currant-juice") names
        /// that product, never the first whole food: the first food's own product ("apple
        /// marmalade") when the catalog holds it, else the product an "any other" alternative names
        /// ("marmalade"), else nothing. When the first food's product and a later alternative are
        /// both foods ("avocado or olive oil"), the line is ambiguous as before. Null when the line
        /// is not such a list.
        /// </summary>
        private static CompoundPlan? SharedProduct(string line, string wording, List<string> names, long firstFood, CompoundVocabulary v)
        {
            var at = names.FindIndex(IsProduct);
            if (at < 1 || !v.CountedInPieces(firstFood)) return null;
            var products = names.Skip(at).Select(n => Purpose.Replace(n, string.Empty)).ToList();
            if (!products.All(IsProduct) || !products.Any(p => Words(p).Length > 1)) return null;
            if (!names.Take(at).All(n => IsSingleWord(n) && !n.Contains('-'))) return null;

            foreach (var candidate in products.Select(p => $"{names[0]} {Words(p)[^1]}"))
            {
                if (v.Resolve(candidate) is not { } product || product == firstFood) continue;
                if (products.Any(p => !AnyOther.IsMatch(p) && v.Resolve(p) is { } named && named != product)) return CompoundPlan.Leave(wording, LeaveOrAmbiguous);
                return new CompoundPlan(OrSharedHead, CompoundKind.PickAlternative, new[] { new CompoundPart(candidate, product, line, null) }, wording, null);
            }

            foreach (var kind in products.Where(p => AnyOther.IsMatch(p)).Select(p => AnyOther.Replace(p, string.Empty)))
            {
                if (v.Resolve(kind) is { } product && product != firstFood)
                {
                    return new CompoundPlan(OrSharedHead, CompoundKind.PickAlternative, new[] { new CompoundPart(kind, product, line, null) }, wording, null);
                }
            }

            return CompoundPlan.Leave(wording, LeaveSharedProduct);
        }

        private static string[] Words(string name) => name.Split(WordBreaks, StringSplitOptions.RemoveEmptyEntries).Select(w => w.Trim('.', ';', ':')).ToArray();

        private static bool IsProduct(string name) => Words(name) is { Length: > 0 } words && ProductWords.Contains(words[^1]);

        /// <summary>
        /// Whether a bare first alternative and the first word of a longer last one are both whole
        /// foods counted in pieces ("tangerine or orange juice", "avocado or olive oil", "apricot or
        /// peach-stones"): the last word is then shared, and the first alternative means its
        /// product, not the whole food.
        /// </summary>
        private static bool WholeFoodBesideProduct(string first, string last, CompoundVocabulary v)
        {
            var words = last.Split(WordBreaks, StringSplitOptions.RemoveEmptyEntries);
            return IsSingleWord(first) && !first.Contains('-') && words.Length >= 2
                && v.Resolve(first) is { } whole && v.CountedInPieces(whole)
                && v.Resolve(words[0]) is { } other && v.CountedInPieces(other);
        }

        private static CompoundPlan AndList(string line, string wording, List<string> names, CompoundVocabulary v)
        {
            var foods = names.Select(v.Resolve).ToList();
            var sharesHead = SharesHead(names[0], names[^1], v);
            if (foods.Any(f => f == null) || sharesHead) return CompoundPlan.Leave(wording, sharesHead ? LeaveSharedHead : LeaveNotInCatalog);

            if (foods.Distinct().Count() != foods.Count) return CompoundPlan.Leave(wording, LeaveOneFood);

            if (Locate(line, names) is not { } spans) return CompoundPlan.Leave(wording, LeaveUnlocated);
            var prefix = line[..spans[0].Start];
            var suffix = line[spans[^1].End..];
            var each = Each.IsMatch(prefix);
            if (ListInPrefix.IsMatch(prefix)) return CompoundPlan.Leave(wording, each ? LeaveEachWithoutList : LeaveUnlocated);

            var quantity = IngredientLineParser.Parse(line);
            var isPair = foods.Count == 2 && v.SaltId is { } salt && v.PepperId is { } pepper
                && foods.Contains(salt) && foods.Contains(pepper);
            var written = spans.Select(s => line[s.Start..s.End]).ToList();
            if (each && quantity == null) return CompoundPlan.Leave(wording, LeaveEachUnread);
            if (written.Skip(1).Any(w => CountStart.IsMatch(w) || HalfStart.IsMatch(w) || ListMeasure.IsMatch(w))) return CompoundPlan.Leave(wording, LeaveQuantifiedList);
            if (v.PepperId is { } spice && foods.Select((f, i) => (f, i)).Any(p => p.f == spice && CutWord.IsMatch(written[p.i]))) return CompoundPlan.Leave(wording, LeaveCutPepper);
            if (written.Any(w => IngredientNameNormalizer.Normalize(w).Equals("mustard", StringComparison.OrdinalIgnoreCase))
                && (foods.Any(f => f == v.SaltId || f == v.PepperId) || written.Any(IsSpiceOrSugar)))
            {
                return CompoundPlan.Leave(wording, LeaveSpiceMustard);
            }

            string rule;
            List<CompoundPart> parts;
            if (quantity == null)
            {
                rule = isPair ? AndSaltPepper : AndUnquantified;
                parts = names.Select((n, i) => new CompoundPart(n, foods[i]!.Value, Join(prefix, written[i], suffix), null)).ToList();
            }
            else if (each)
            {
                rule = AndEach;
                var shared = Each.Replace(prefix, string.Empty);
                parts = names.Select((n, i) => new CompoundPart(n, foods[i]!.Value, Join(shared, written[i], suffix), quantity)).ToList();
            }
            else if (isPair)
            {
                rule = AndQuantifiedSaltPepper;
                parts = names.Select((n, i) => foods[i] == v.SaltId
                        ? new CompoundPart(n, foods[i]!.Value, Join(prefix, written[i], suffix), quantity)
                        : new CompoundPart(n, foods[i]!.Value, Join(string.Empty, written[i], suffix), null))
                    .ToList();
            }
            else
            {
                return CompoundPlan.Leave(wording, LeaveQuantifiedList);
            }

            return Checked(new CompoundPlan(rule, CompoundKind.Split, parts, wording, null), line, v, strictFood: true);
        }

        private static bool IsSpiceOrSugar(string written) =>
            Regex.Matches(written, @"\p{L}+").Any(w => Spices.Contains(w.Value) || MustardCompanions.Contains(w.Value) || w.Value.Equals("ground", StringComparison.OrdinalIgnoreCase));

        private static CompoundPlan? OwnQuantityPlan(string line, string wording, CompoundVocabulary v)
        {
            var segments = new List<string>();
            var at = 0;
            foreach (Match separator in OwnSeparator.Matches(line))
            {
                if (line[..separator.Index].Count(c => c == '(') != line[..separator.Index].Count(c => c == ')')) return null;
                segments.Add(line[at..separator.Index].Trim());
                at = separator.Index + separator.Length;
            }

            segments.Add(line[at..].Trim());
            if (segments.Count < 2 || segments.Count > 4 || !segments.All(s => LeadingNumber.IsMatch(s))) return null;

            var parts = new List<CompoundPart>();
            foreach (var segment in segments)
            {
                var amount = IngredientLineParser.Parse(segment);
                var name = IngredientNameNormalizer.Normalize(segment);
                if (amount == null || v.Resolve(name) is not { } food) return null;
                if (amount.MeasurementName == IngredientLineParser.Piece && !v.CountedInPieces(food)) return CompoundPlan.Leave(wording, LeaveBareNumber);
                parts.Add(new CompoundPart(name, food, segment, amount));
            }

            if (parts.Select(p => p.FoodId).Distinct().Count() != parts.Count) return null;
            return Checked(new CompoundPlan(OwnQuantity, CompoundKind.Split, parts, wording, null), line, v, strictFood: true);
        }

        private static CompoundPlan? PartPairPlan(string line, CompoundVocabulary v)
        {
            var match = PartPairOf.Match(line);
            var after = !match.Success;
            if (after) match = PartPairAfter.Match(line);
            if (!match.Success) return null;

            var p1 = match.Groups["p1"];
            var p2 = match.Groups["p2"];
            var isJuice1 = p1.Value.Equals("juice", StringComparison.OrdinalIgnoreCase);
            var isJuice2 = p2.Value.Equals("juice", StringComparison.OrdinalIgnoreCase);
            if (isJuice1 == isJuice2) return null;

            var fruit = match.Groups["fruit"].Value.ToLowerInvariant();
            var wording = $"{fruit} {p1.Value.ToLowerInvariant()} and {p2.Value.ToLowerInvariant()}";
            var juice = v.Resolve($"{fruit} juice");
            var peel = PeelNames.Select(p => v.Resolve($"{fruit} {p}")).FirstOrDefault(f => f != null);
            if (juice == null || peel == null) return CompoundPlan.Leave(wording, LeaveNotInCatalog);
            if (after && IngredientLineParser.Parse(line) is { } amount && amount.MeasurementName != IngredientLineParser.Piece) return CompoundPlan.Leave(wording, LeaveNotFixedPoint);

            var firstLine = Tidy(line.Remove(p1.Index + p1.Length, p2.Index + p2.Length - (p1.Index + p1.Length)));
            var from = isJuice2 && match.Groups["adj"].Success ? match.Groups["adj"].Index : p1.Index;
            var secondLine = Tidy(line.Remove(from, match.Groups["g"].Index - from));
            var parts = new[]
            {
                new CompoundPart($"{fruit} {p1.Value.ToLowerInvariant()}", isJuice1 ? juice.Value : peel.Value, firstLine, IngredientLineParser.Parse(firstLine)),
                new CompoundPart($"{fruit} {p2.Value.ToLowerInvariant()}", isJuice2 ? juice.Value : peel.Value, secondLine, IngredientLineParser.Parse(secondLine)),
            };
            if (!Same(parts[0].Quantity, parts[1].Quantity) || !parts.All(p => NamesOnlyPart(p.RawLine, fruit))) return CompoundPlan.Leave(wording, LeaveNotFixedPoint);
            return Checked(new CompoundPlan(PartPair, CompoundKind.Split, parts, wording, null), line, v, strictFood: false);
        }

        private static bool NamesOnlyPart(string partLine, string fruit) =>
            Regex.Matches(IngredientNameNormalizer.Normalize(partLine), @"\p{L}+")
                .All(w => PartWords.Contains(w.Value) || w.Value.Equals(fruit, StringComparison.OrdinalIgnoreCase) || w.Value.Equals(fruit + "s", StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Where each name sits in the line, in order, or null when one cannot be found as written
        /// after the one before it.
        /// </summary>
        private static List<(int Start, int End)>? Locate(string line, List<string> names)
        {
            var spans = new List<(int Start, int End)>();
            var from = 0;
            foreach (var name in names)
            {
                var start = WordAt(line, name, from);
                if (start < 0) return null;
                spans.Add((start, start + name.Length));
                from = start + name.Length;
            }

            return spans;
        }

        private static int WordAt(string line, string name, int from)
        {
            var start = line.IndexOf(name, from, StringComparison.OrdinalIgnoreCase);
            while (start >= 0 && ((start > 0 && char.IsLetter(line[start - 1])) || (start + name.Length < line.Length && char.IsLetter(line[start + name.Length]))))
                start = line.IndexOf(name, start + 1, StringComparison.OrdinalIgnoreCase);
            return start;
        }

        private static string Join(string prefix, string name, string suffix) => Tidy(prefix + name + suffix);

        private static string Tidy(string text) => Whitespace.Replace(text, " ").Trim();

        private static bool Same(ParsedQuantity? a, ParsedQuantity? b) =>
            a == null ? b == null : b != null && a.Quantity == b.Quantity && a.MeasurementName.Equals(b.MeasurementName, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Keeps a split only when every part line is a fixed point of the pipeline: the line
        /// parser reads from it exactly the amount its row stores, its wording names its food (or,
        /// for a part pair, no other food), an acceptably unquantified source gives acceptably
        /// unquantified unmeasured parts, no part repeats another's text, and a part of a list
        /// counted in pieces starts with a count and names no measure ("a very little salt" and "1 quarter
        /// teaspoonful of salt" are read as one piece, which is not what they say).
        /// </summary>
        private static CompoundPlan Checked(CompoundPlan plan, string line, CompoundVocabulary v, bool strictFood)
        {
            var acceptable = RecipeVettingService.IsAcceptablyUnquantified(line);
            if (plan.Parts.Select(p => p.RawLine).Distinct(StringComparer.OrdinalIgnoreCase).Count() != plan.Parts.Count) return CompoundPlan.Leave(plan.Wording, LeaveNotFixedPoint);
            foreach (var part in plan.Parts)
            {
                if (!Same(IngredientLineParser.Parse(part.RawLine), part.Quantity)) return CompoundPlan.Leave(plan.Wording, LeaveNotFixedPoint);
                if (strictFood && part.Quantity is { MeasurementName: IngredientLineParser.Piece }
                    && (!CountStart.IsMatch(part.RawLine) || MeasureWord.IsMatch(part.RawLine))) return CompoundPlan.Leave(plan.Wording, LeaveNotFixedPoint);
                if (strictFood && part.Quantity is { MeasurementName: IngredientLineParser.Piece } && !v.CountedInPieces(part.FoodId)) return CompoundPlan.Leave(plan.Wording, LeaveBareNumber);
                var food = v.Resolve(IngredientNameNormalizer.Normalize(part.RawLine));
                if (strictFood ? food != part.FoodId : food != null && food != part.FoodId) return CompoundPlan.Leave(plan.Wording, LeaveNotFixedPoint);
                if (acceptable && part.Quantity == null && !RecipeVettingService.IsAcceptablyUnquantified(part.RawLine)) return CompoundPlan.Leave(plan.Wording, LeaveNotFixedPoint);
            }

            return plan;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Nom.Orch.Services.Support
{
    /// <summary>A curated food as the compound-line rules see it; <paramref name="CountedInPieces"/> when it has a weight per item.</summary>
    public sealed record CompoundFood(long Id, string Name, bool HasNutrition, bool CountedInPieces = true);

    /// <summary>An alias of a food and the context it was recorded under.</summary>
    public sealed record CompoundAlias(long IngredientId, string AliasName, string? SourceContext);

    /// <summary>
    /// The names <see cref="CompoundLineSplitter"/> may read a part of a recipe line as. A part
    /// resolves only to a live curated food with nutrition, by its name, by an alias left by a
    /// retirement, by the USDA description a staple pantry name means, or as a plain salt or
    /// black pepper. Aliases recorded without a context are import residue and never resolve, and
    /// nothing is matched loosely.
    /// </summary>
    public sealed class CompoundVocabulary
    {
        private static readonly Regex SaltForm = new(
            @"^(?:(?:fine|coarse|coarsely|flaky|table|kosher|sea|ground|iodized)\s+)*salt$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex PepperForm = new(
            @"^(?:(?:freshly|fresh)\s+)?(?:(?:ground|cracked|crushed|milled)\s+)?(?:black\s+)?pepper$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex Conjunction = new(
            @"(?<![\p{L}])(?:and|or)(?![\p{L}])|&", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private const string SaltDescription = "Salt, table";
        private const string PepperDescription = "Spices, pepper, black";

        /// <summary>Names that hold "and", "or" or "&amp;" and are one food or one product.</summary>
        public static readonly IReadOnlyList<string> ProtectedSeed = new[]
        {
            "half and half", "half & half", "half-and-half", "half n half", "half 'n half", "half 'n' half",
            "macaroni and cheese", "mac and cheese", "mac & cheese", "mac n cheese", "pork and beans", "franks and beans",
            "sweet and sour", "sweet & sour", "hot and sour", "salt and vinegar", "sour cream and onion", "bread and butter",
            "cookies and cream", "cookies & cream", "peanut butter and jelly", "fish and chips", "rice and beans",
            "ham and cheese", "lea & perrins", "lea and perrins", "m&m", "m & m", "m&ms", "arm & hammer", "a&w",
            "surf and turf", "spaghetti and meatballs", "biscuits and gravy", "liver and onions", "steak and kidney",
            "chicken and dumplings", "corned beef and cabbage", "fruit and nut", "oil and vinegar dressing",
            "mustard and cress", "mustard & cress",
        };

        private readonly Dictionary<string, long> _foods;
        private readonly Dictionary<string, long> _usda;
        private readonly HashSet<string> _known;
        private readonly List<Regex> _protected;
        private readonly HashSet<long> _pieces;

        private CompoundVocabulary(Dictionary<string, long> foods, Dictionary<string, long> usda, HashSet<string> known, IEnumerable<string> protectedNames, HashSet<long> pieces)
        {
            _foods = foods;
            _pieces = pieces;
            _usda = usda;
            _known = known;
            ProtectedNames = protectedNames.Select(p => p.ToLowerInvariant()).Distinct().OrderBy(p => p, StringComparer.Ordinal).ToList();
            _protected = ProtectedNames
                .Select(p => new Regex(@"(?<![\p{L}\p{N}])" + Regex.Escape(p).Replace(@"\ ", @"[\s-]+") + @"(?![\p{L}\p{N}])",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                .ToList();
            SaltId = usda.TryGetValue(SaltDescription, out var salt) ? salt : null;
            PepperId = usda.TryGetValue(PepperDescription, out var pepper) ? pepper : null;
        }

        /// <summary>The staple salt food, or null when the catalog has none.</summary>
        public long? SaltId { get; }

        /// <summary>The staple black pepper food, or null when the catalog has none.</summary>
        public long? PepperId { get; }

        /// <summary>Lower-cased names a compound line is never split inside.</summary>
        public IReadOnlyList<string> ProtectedNames { get; }

        /// <summary>
        /// Builds the vocabulary from the curated foods, their aliases and the names of every live
        /// ingredient. Only foods with nutrition are targets; every curated name made of a short
        /// "a and b" first segment protects that segment.
        /// </summary>
        public static CompoundVocabulary Build(IEnumerable<CompoundFood> curated, IEnumerable<CompoundAlias> aliases, IEnumerable<string> allNames)
        {
            var foods = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            var usda = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            var protectedNames = new List<string>(ProtectedSeed);
            var live = new HashSet<long>();
            var pieces = new HashSet<long>();
            foreach (var food in curated.OrderBy(f => f.Id))
            {
                if (food.CountedInPieces) pieces.Add(food.Id);
                var name = food.Name.Trim();
                var segment = name.Split(',')[0].Trim();
                if (Conjunction.IsMatch(segment) && segment.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 5) protectedNames.Add(segment);
                if (!food.HasNutrition) continue;
                live.Add(food.Id);
                foods.TryAdd(name, food.Id);
                usda.TryAdd(name, food.Id);
            }

            foreach (var alias in aliases.Where(a => live.Contains(a.IngredientId)).OrderBy(a => a.IngredientId))
            {
                if (alias.SourceContext == null) continue;
                if (alias.SourceContext.StartsWith(Services.LineRelinkService.RetiredContextPrefix, StringComparison.Ordinal)) foods.TryAdd(alias.AliasName.Trim(), alias.IngredientId);
                else if (alias.SourceContext == Services.IngredientLinkService.UsdaDescriptionContext) usda.TryAdd(alias.AliasName.Trim(), alias.IngredientId);
            }

            var known = new HashSet<string>(allNames.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim().ToLowerInvariant()), StringComparer.Ordinal);
            return new CompoundVocabulary(foods, usda, known, protectedNames, pieces);
        }

        /// <summary>Whether the food is counted in pieces, so a bare number in front of it is a count.</summary>
        public bool CountedInPieces(long foodId) => _pieces.Contains(foodId);

        /// <summary>
        /// The curated food a part of a line names, or null. Tried in order: the part and its
        /// singular, plural and bare forms by name; the USDA entry a staple pantry name means; a
        /// salt or black pepper form; then the same with hyphens read as spaces.
        /// </summary>
        public long? Resolve(string part)
        {
            if (string.IsNullOrWhiteSpace(part)) return null;
            return ResolveOnce(part) ?? (part.Contains('-') ? ResolveOnce(part.Replace('-', ' ')) : null);
        }

        private long? ResolveOnce(string part)
        {
            foreach (var key in IngredientNameNormalizer.LookupKeys(part))
                if (_foods.TryGetValue(key, out var id)) return id;

            foreach (var name in new[] { part, IngredientNameNormalizer.Bare(part) })
                if (StapleFoods.UsdaNameFor(name) is { } description && _usda.TryGetValue(description, out var staple)) return staple;

            var plain = Regex.Replace(IngredientNameNormalizer.Normalize(part).ToLowerInvariant(), @"\s+", " ").Trim();
            if (SaltForm.IsMatch(plain)) return SaltId;
            if (PepperForm.IsMatch(plain)) return PepperId;
            return null;
        }

        /// <summary>Whether a lower-cased name is the name of any live ingredient, curated or not.</summary>
        public bool IsKnown(string name) => _known.Contains(name.Trim().ToLowerInvariant());

        /// <summary>Whether a wording holds a name that is one food or one product.</summary>
        public bool HoldsProtected(string wording) => _protected.Any(p => p.IsMatch(wording));
    }
}

using System;
using System.Collections.Generic;
using System.Linq;

namespace Nom.Orch.Services.Support
{
    /// <summary>
    /// The rules of the link lane around its model. Before it is asked: a wording that names two
    /// foods ("butter or margarine", "salt and pepper") is no one USDA food, so it is linked to none,
    /// while two variants of one food ("light or dark brown sugar", "fresh or frozen corn", "peeled and
    /// sliced squash") and an amount given another way ("an onion or two") are still asked; and a
    /// staple named with sizes, praise or preparation words is the staple. After it answers: a pick
    /// that contradicts the wording is refused. <see cref="Version"/> is part of the lane's signature.
    /// </summary>
    public static class LinkPickRules
    {
        public const string Version = "pick-rules/1";
        public const string TwoFoods = "No USDA match: " + LinkAutoApplyGuard.CompoundRule;

        private static readonly HashSet<string> Joiners = new(StringComparer.Ordinal) { "and", "or", "&", "+" };

        /// <summary>Words that tell two variants of one food apart; a joiner between two of them joins variants, not foods.</summary>
        internal static readonly HashSet<string> Variants = new(StringComparer.Ordinal)
        {
            "red", "white", "yellow", "green", "black", "brown", "golden", "purple", "dark", "light", "fresh", "frozen", "canned", "bottled",
            "dried", "dry", "homemade", "store-bought", "storebought", "regular", "mini", "instant", "active", "quick", "rolled", "unrefined",
            "refined", "clear", "filtered", "semi-sweet", "semisweet", "bittersweet", "low-fat", "lowfat", "reduced-fat", "nonfat", "fat-free",
            "skim", "smoked", "plain", "slivered", "parboiled", "pounded", "dredged", "deveined", "smashed", "snipped", "unbeaten",
        };

        /// <summary>Words after "or" that give the same food another way: an amount ("or two", "or 2 teaspoons dried"), more of it, any kind of it, a substitute.</summary>
        private static readonly HashSet<string> SameFood = new(StringComparer.Ordinal)
        {
            "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "twelve", "a", "an", "more", "less", "any", "substitute", "so",
        };

        /// <summary>Why a normalised wording is linked to no food without asking the model, or null when the model is asked.</summary>
        public static string? RuleNone(string name) => NamesTwoFoods(name) ? TwoFoods : null;

        /// <summary>
        /// Whether the food part of a wording (before its first comma or colon note) joins two foods:
        /// a "+" or ";", or an "and", "&amp;" or "or" whose neighbouring words are not both
        /// preparations or variants of one food, and that does not give an amount or a substitute.
        /// </summary>
        public static bool NamesTwoFoods(string name)
        {
            var text = name.ToLowerInvariant();
            foreach (var one in LinkAutoApplyGuard.OneFoodNames) text = text.Replace(one, one.Replace(' ', '_').Replace("&", "_").Replace('-', '_'));
            if (text.Contains(':')) text = text[(text.LastIndexOf(':') + 1)..];
            var segments = text.Split(',');
            var food = segments[0];
            if (food.IndexOfAny(new[] { '+', ';' }) >= 0) return true;
            if (segments.Skip(1).Select(s => s.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Any(s => s.Length > 1 && (s[0] == "or" || s[0] == "and") && !Variant(s[1]) && !SameAmount(s[1]))) return true;
            var words = food.Replace("&", " & ").Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => w.Trim('.', '(', ')')).Where(w => w.Length > 0).ToList();
            while (words.Count > 0 && Joiners.Contains(words[0])) words.RemoveAt(0);
            for (var i = 1; i < words.Count - 1; i++)
            {
                if (!Joiners.Contains(words[i])) continue;
                var before = words.Take(i).LastOrDefault(w => w != "slightly" && w != "very" && w != "well");
                var after = words.Skip(i + 1).FirstOrDefault(w => w != "slightly" && w != "very" && w != "well");
                if (before == null || after == null) continue;
                if (words[i] == "or" && SameAmount(after)) continue;
                if (Variant(before) && Variant(after)) continue;
                return true;
            }
            return false;
        }

        /// <summary>
        /// The food a wording means without asking the model when, with its sizes, praise and
        /// preparation words left out ("good-sized onion", "butter the size of an egg", "melted
        /// butter, divided"), it is a bare staple name (<see cref="StapleFoods"/>); null otherwise.
        /// A note after a comma that holds any other word leaves the wording to the model.
        /// </summary>
        public static (FoodCandidate Food, string Source)? RuleTarget(string name, IReadOnlyDictionary<string, FoodCandidate> staples)
        {
            var stripped = Plain(name);
            if (stripped == null || stripped.Length == 0 || stripped == name.Trim().ToLowerInvariant()) return null;
            return StapleFoods.UsdaNameFor(stripped) is { } usda && staples.TryGetValue(usda, out var staple) ? (staple, PlainWordsSource) : null;
        }

        public const string PlainWordsSource = "deterministic:plain-words";

        private static readonly System.Text.RegularExpressions.Regex SizeOf = new(
            @"\b(?:the )?size of (?:a |an |half an? |two |three )?(?:small |large |hen'?s |pigeon'?s )?(?:egg|walnut|nut|nutmeg|hazelnut|filbert)s?\b",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant | System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>Sizes, praise, what the cook does, and the grain or source of a salt or water: words that never change which staple is meant.</summary>
        internal static readonly HashSet<string> PlainNoise = new(StringComparer.Ordinal)
        {
            "small", "large", "medium", "big", "little", "sized", "middling", "dozen", "good", "best", "very", "nice", "fine", "chopped", "minced",
            "diced", "sliced", "grated", "shredded", "melted", "softened", "soft", "beaten", "sifted", "peeled", "cubed", "halved", "quartered",
            "divided", "drained", "rinsed", "trimmed", "packed", "firmly", "loosely", "finely", "coarsely", "roughly", "thinly", "freshly",
            "lightly", "well", "cut", "chilled", "cold", "room", "temperature", "at", "for", "garnish", "serving", "spreading", "optional", "plus",
            "more", "about", "separated", "into", "pieces", "cubes", "unbeaten", "slightly", "thin", "thick", "up", "in", "inch", "the", "of", "a", "an",
            "kosher", "coarse", "flaked", "flaky", "sea", "ice",
        };

        /// <summary>The wording with its noise words left out, or null when a note after a comma holds another word.</summary>
        internal static string? Plain(string name)
        {
            var segments = SizeOf.Replace(name.ToLowerInvariant(), " ").Split(',');
            static IEnumerable<string> Words(string s) => s.Replace('-', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => w.Trim('.', '(', ')', '!', ';')).Where(w => w.Length > 0);
            if (segments.Skip(1).SelectMany(Words).Any(w => !PlainNoise.Contains(w))) return null;
            return string.Join(' ', Words(segments[0]).Where(w => !PlainNoise.Contains(w)));
        }

        private static readonly System.Text.RegularExpressions.Regex FreezeDried = new(@"\bfreeze[- ]dried\b", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        private static readonly System.Text.RegularExpressions.Regex Unsweetened = new(@"\b(unsweetened|not sweetened|no sugar added|without added sugar)\b", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        private static readonly System.Text.RegularExpressions.Regex Sweetened = new(@"(?<!\bun|\bnot )\bsweetened\b", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        private static readonly System.Text.RegularExpressions.Regex CookedWord = new(@"\b(cooked|boiled)\b", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        private static readonly System.Text.RegularExpressions.Regex RawWord = new(@"\b(raw|uncooked)\b", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        /// <summary>
        /// Why the model's pick contradicts the wording, so the lane links nothing instead, or null:
        /// the wording names a freeze-dried food and the food is not; one of the two is unsweetened and
        /// the other sweetened; or the wording names a cooked or boiled food and the food is raw.
        /// </summary>
        public static string? Contradicts(string name, FoodCandidate pick)
        {
            var wording = name.ToLowerInvariant();
            var food = pick.Name.ToLowerInvariant();
            if (FreezeDried.IsMatch(wording) && !FreezeDried.IsMatch(food)) return "the wording is freeze-dried and the food is not";
            if (Unsweetened.IsMatch(wording) && !Unsweetened.IsMatch(food) && Sweetened.IsMatch(food)) return "the wording is unsweetened and the food sweetened";
            if (Sweetened.IsMatch(wording) && !Unsweetened.IsMatch(wording) && Unsweetened.IsMatch(food)) return "the wording is sweetened and the food unsweetened";
            if (CookedWord.IsMatch(wording) && !RawWord.IsMatch(wording) && !wording.Contains("dry", StringComparison.Ordinal) && RawWord.IsMatch(food) && !CookedWord.IsMatch(food))
                return "the wording is cooked and the food raw";
            return null;
        }

        private static bool SameAmount(string word) => SameFood.Contains(word) || char.IsNumber(word[0]);

        private static bool Variant(string word) => Variants.Contains(word) || LinkAutoApplyGuard.Notes.Contains(word) || LinkAutoApplyGuard.PrepWords.Contains(word);
    }
}

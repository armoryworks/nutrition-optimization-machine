using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Nom.Orch.Interfaces;

namespace Nom.Orch.Services.Support
{
    /// <summary>
    /// The rules of the link lane's judge around its model. Before it is asked: a wording that names
    /// two foods (<see cref="LinkPickRules.NamesTwoFoods"/>) is more than one food without asking.
    /// After it answers: a "same" that the pair contradicts is refused as a variety or form (the
    /// wording names a freeze-dried, sweetened, unsweetened or cooked food the description is not;
    /// the description is a cooked meat, a light butter or spread, a pressurized topping or a soy
    /// product the wording does not name), and a "same" for a pair whose wording and description
    /// share no word is refused as a different food (a synonym, a translation or an old name is the
    /// wording lanes' to settle). The rules only ever withhold a "same"; they never verify.
    /// <see cref="Version"/> is part of the lane's signature.
    /// </summary>
    public static class LinkVerifyRules
    {
        public const string Version = "verify-rules/2";

        private static readonly Regex Letters = new(@"\p{L}+", RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly HashSet<string> Filler = new(StringComparer.Ordinal)
        {
            "the", "and", "with", "for", "fresh", "large", "small", "medium", "good", "fine", "best", "chopped", "sliced", "diced", "minced",
            "grated", "cold", "hot", "warm", "boiling", "melted", "softened", "beaten", "cup", "cups", "little", "few", "whole", "raw", "cooked", "plain",
        };

        private static readonly Regex Cooking = new(@"\b(cooked|roasted|fried|stewed|boiled|braised|grilled|broiled|sauteed|steamed|baked|simmered|rotisserie)\b",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex CookedWording = new(
            @"\b(cook|cooked|roast|roasted|fried|fry|baked|stewed|boiled|braised|grilled|broiled|sauteed|steamed|leftover|left-over|rotisserie|smoked|cured|corned|barbecued|bbq|canned|deli|prepared|scrambled|poached|seared|shredded|pulled|hard-boiled)\b",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex LightWording = new(@"\b(light|lite|reduced|low|lower|diet|half-fat)\b", RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex PressurizedWording = new(@"\b(pressurized|aerosol|canned|can|spray|squirty|reddi)\b", RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly string[] Meats =
        {
            "beef", "pork", "lamb", "mutton", "veal", "goat", "chicken", "turkey", "duck", "goose", "guinea hen", "game meat", "deer", "elk", "bison",
            "rabbit", "fish", "crustaceans", "mollusks", "egg", "eggs",
        };

        private static readonly string[] Spreads = { "butter", "margarine", "cream cheese", "sour cream", "mayonnaise", "ice cream", "salad dressing", "cheese", "cream substitute", "nuts, coconut milk" };

        /// <summary>The verdict a wording gets without asking the model, or null when the model is asked.</summary>
        public static LinkJudgement? Before(string wording) =>
            LinkPickRules.NamesTwoFoods(wording.ToLowerInvariant()) ? LinkJudgement.MoreThanOneFood : null;

        /// <summary>Why a "same" for the pair is refused, or null when it stands.</summary>
        public static string? Refuses(string wording, string description)
        {
            if (LinkPickRules.Contradicts(wording, new FoodCandidate(0, string.Empty, description, string.Empty)) is { } contradiction) return contradiction;
            var w = wording.ToLowerInvariant();
            var d = description.ToLowerInvariant();
            var segments = d.Split(',').Select(s => s.Trim()).ToArray();
            if (Meats.Any(m => segments[0] == m || segments[0].StartsWith(m + " ", StringComparison.Ordinal)) && Cooking.IsMatch(d) && !CookedWording.IsMatch(w))
                return "the food is a cooked meat and the wording names no cooking";
            if (segments.Skip(1).Contains("light") && Spreads.Any(s => d.StartsWith(s, StringComparison.Ordinal)) && !LightWording.IsMatch(w))
                return "the food is a light product and the wording is not";
            if (d.Contains("pressurized", StringComparison.Ordinal) && !PressurizedWording.IsMatch(w))
                return "the food is a pressurized topping and the wording is not";
            if (d.Contains("made from soy", StringComparison.Ordinal) && !w.Contains("soy", StringComparison.Ordinal))
                return "the food is made from soy and the wording is not";
            return null;
        }

        /// <summary>
        /// Whether the wording and the description share no word: no word of three letters or more of
        /// the wording, sizes and the cook's work left out and a plural "s" dropped, equals a word of the
        /// description or, at four letters or more, holds one or is held in one ("mint" in "spearmint").
        /// A wording with no such word is never said to share none.
        /// </summary>
        public static bool SharesNoWord(string wording, string description)
        {
            var mine = Words(wording);
            if (mine.Count == 0) return false;
            var theirs = Words(description);
            return !mine.Any(a => theirs.Any(b => a == b || (a.Length >= 4 && b.Length >= 4 && (a.Contains(b, StringComparison.Ordinal) || b.Contains(a, StringComparison.Ordinal)))));
        }

        private static List<string> Words(string text) =>
            Letters.Matches(text.ToLowerInvariant()).Select(m => m.Value).Where(w => w.Length >= 3 && !Filler.Contains(w))
                .Select(w => w.Length > 3 && w.EndsWith('s') ? w[..^1] : w).Distinct().ToList();

        /// <summary>
        /// The model's verdict with the rules applied: a "same" a rule refuses becomes a variety or form,
        /// and a "same" for a pair that shares no word a different food.
        /// </summary>
        public static LinkJudgement After(string wording, string description, LinkJudgement judgement) =>
            judgement != LinkJudgement.Same ? judgement
            : Refuses(wording, description) != null ? LinkJudgement.VarietyOrForm
            : SharesNoWord(wording, description) ? LinkJudgement.DifferentFood
            : judgement;
    }
}

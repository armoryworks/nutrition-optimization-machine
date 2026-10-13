using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Nom.Orch.Services.Support
{
    /// <summary>
    /// Turns equipment nouns the tagging model proposes into a stable key ("Pizza Stones" →
    /// "pizza stone") and rejects everyday utensils that every kitchen has, so the suggestion
    /// queue holds only genuinely uncommon equipment.
    /// </summary>
    public static class ToolNameNormalizer
    {
        private static readonly HashSet<string> Everyday = new(StringComparer.Ordinal)
        {
            "knife", "chef knife", "chef's knife", "paring knife", "bread knife", "cutting board", "board",
            "bowl", "mixing bowl", "small bowl", "large bowl", "spoon", "wooden spoon", "slotted spoon", "spatula",
            "rubber spatula", "whisk", "ladle", "tongs", "measuring cup", "measuring spoon", "colander", "strainer",
            "sieve", "fine mesh strainer", "pot", "large pot", "stockpot", "stock pot", "pan", "saucepan", "sauce pan",
            "skillet", "frying pan", "nonstick skillet", "lid", "plate", "platter", "cup", "glass", "mug", "fork",
            "peeler", "vegetable peeler", "grater", "box grater", "can opener", "plastic wrap", "foil", "aluminum foil",
            "parchment paper", "parchment", "wax paper", "paper towel", "towel", "kitchen towel", "oven mitt",
            "rack", "wire rack", "cooling rack", "baking sheet", "sheet pan", "cookie sheet", "toothpick", "skewer",
            "zip top bag", "ziploc bag", "plastic bag", "bag", "jar", "container", "airtight container", "tray",
            "rolling pin", "pastry brush", "brush", "scissors", "kitchen scissors", "kitchen shears", "shears",
            "timer", "stove", "burner", "sink", "refrigerator", "fridge", "freezer", "counter", "countertop",
            "serving dish", "serving bowl", "casserole", "dish", "baking pan", "cake pan", "pie dish", "pie plate",
            "ramekin", "mortar", "pestle", "funnel", "baster", "garlic press", "zester", "microplane", "pitcher",
        };

        private static readonly HashSet<string> PluralForms = new(StringComparer.Ordinal)
        {
            "tongs", "shears", "scissors", "pliers", "chopsticks", "tweezers", "glass", "press", "dutch oven",
        };

        private static readonly Regex Clean = new(@"\(.*?\)|[^a-z0-9'\- ]", RegexOptions.Compiled);
        private static readonly Regex Spaces = new(@"\s+", RegexOptions.Compiled);
        private static readonly Regex LeadingArticle = new(@"^(a|an|the|some|large|small|medium|big|heavy|good|sturdy|oven safe|ovenproof|oven proof|nonstick|non stick|heavy bottomed|heavy duty|deep|shallow|rimmed|lined|greased|lightly|clean|dry|wide|tall|round|square|rectangular|wooden|metal|glass|ceramic|stainless steel|stainless|steel)\s+", RegexOptions.Compiled);

        /// <summary>The normalized key, or null when the noun is unusable or everyday equipment.</summary>
        public static string? Normalize(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;

            var s = Clean.Replace(raw.ToLowerInvariant().Replace('-', ' '), " ");
            s = Spaces.Replace(s, " ").Trim(' ', '-', '\'');
            for (var previous = ""; previous != s;)
            {
                previous = s;
                s = LeadingArticle.Replace(s, "");
            }

            if (s.Length < 3 || s.Length > 40 || s.Any(char.IsDigit) || s.Split(' ').Length > 4) return null;

            s = Singular(s);
            return Everyday.Contains(s) ? null : s;
        }

        /// <summary>Title-cased display name for a normalized key ("pizza stone" → "Pizza stone").</summary>
        public static string Display(string key) => char.ToUpperInvariant(key[0]) + key[1..];

        private static string Singular(string s)
        {
            if (PluralForms.Contains(s)) return s;
            var words = s.Split(' ');
            var last = words[^1];
            if (PluralForms.Contains(last) || last.EndsWith("ss")) return s;

            if (last.EndsWith("ches") || last.EndsWith("shes") || last.EndsWith("xes") || last.EndsWith("sses"))
                last = last[..^2];
            else if (last.EndsWith("ies") && last.Length > 4)
                last = last[..^3] + "y";
            else if (last.EndsWith("s") && last.Length > 3)
                last = last[..^1];

            words[^1] = last;
            return string.Join(' ', words);
        }
    }
}

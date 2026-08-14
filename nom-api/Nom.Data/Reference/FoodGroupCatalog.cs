using System;
using System.Collections.Generic;

namespace Nom.Data.Reference
{
    /// <summary>
    /// Canonical id ↔ display-name mapping for the food-group vocabulary, plus tolerant
    /// resolution of free-text / AI-returned group names to a <see cref="FoodGroupEnum"/>.
    /// Guards against a model hallucinating a group outside the known set.
    /// </summary>
    public static class FoodGroupCatalog
    {
        /// <summary>Canonical display names, keyed by food-group id (matches the seeded reference rows).</summary>
        public static readonly IReadOnlyDictionary<long, string> DisplayNames = new Dictionary<long, string>
        {
            [(long)FoodGroupEnum.Vegetables] = "Vegetables",
            [(long)FoodGroupEnum.Fruits] = "Fruits",
            [(long)FoodGroupEnum.Grains] = "Grains",
            [(long)FoodGroupEnum.ProteinFoods] = "Protein Foods",
            [(long)FoodGroupEnum.Dairy] = "Dairy",
            [(long)FoodGroupEnum.FatsOils] = "Fats/Oils",
            [(long)FoodGroupEnum.Legumes] = "Legumes",
            [(long)FoodGroupEnum.NutsSeeds] = "Nuts/Seeds",
            [(long)FoodGroupEnum.SweetsSnacks] = "Sweets/Snacks",
            [(long)FoodGroupEnum.Beverages] = "Beverages",
        };

        // Normalized synonym/alias → id. Keys are lowercased, alphanumerics only.
        private static readonly Dictionary<string, long> Aliases = BuildAliases();

        /// <summary>
        /// Resolves a free-text group name (display name, enum name, or common synonym) to a
        /// food-group id, or null if it doesn't map to a known group. Punctuation/spacing/case tolerant.
        /// </summary>
        public static long? TryResolve(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var key = Normalize(text);
            if (key.Length == 0) return null;
            return Aliases.TryGetValue(key, out var id) ? id : (long?)null;
        }

        private static Dictionary<string, long> BuildAliases()
        {
            var map = new Dictionary<string, long>();
            void Add(string s, long id) { var k = Normalize(s); if (k.Length > 0) map[k] = id; }

            foreach (var (id, name) in DisplayNames)
            {
                Add(name, id);                          // "Protein Foods"
                Add(name.Replace("/", " "), id);        // "Fats Oils"
            }
            foreach (FoodGroupEnum e in Enum.GetValues(typeof(FoodGroupEnum)))
                Add(e.ToString(), (long)e);             // "ProteinFoods", "FatsOils", "NutsSeeds", "SweetsSnacks"

            // Common singular/synonym forms.
            Add("vegetable", (long)FoodGroupEnum.Vegetables);
            Add("veggies", (long)FoodGroupEnum.Vegetables);
            Add("veg", (long)FoodGroupEnum.Vegetables);
            Add("fruit", (long)FoodGroupEnum.Fruits);
            Add("grain", (long)FoodGroupEnum.Grains);
            Add("protein", (long)FoodGroupEnum.ProteinFoods);
            Add("meat", (long)FoodGroupEnum.ProteinFoods);
            Add("fat", (long)FoodGroupEnum.FatsOils);
            Add("oil", (long)FoodGroupEnum.FatsOils);
            Add("oils", (long)FoodGroupEnum.FatsOils);
            Add("fatsandoils", (long)FoodGroupEnum.FatsOils);
            Add("legume", (long)FoodGroupEnum.Legumes);
            Add("beans", (long)FoodGroupEnum.Legumes);
            Add("nuts", (long)FoodGroupEnum.NutsSeeds);
            Add("seeds", (long)FoodGroupEnum.NutsSeeds);
            Add("nutsandseeds", (long)FoodGroupEnum.NutsSeeds);
            Add("sweets", (long)FoodGroupEnum.SweetsSnacks);
            Add("snacks", (long)FoodGroupEnum.SweetsSnacks);
            Add("sweetsandsnacks", (long)FoodGroupEnum.SweetsSnacks);
            Add("beverage", (long)FoodGroupEnum.Beverages);
            Add("drinks", (long)FoodGroupEnum.Beverages);
            return map;
        }

        private static string Normalize(string s)
        {
            var chars = new List<char>(s.Length);
            foreach (var c in s.ToLowerInvariant())
                if (char.IsLetterOrDigit(c)) chars.Add(c);
            return new string(chars.ToArray());
        }
    }
}

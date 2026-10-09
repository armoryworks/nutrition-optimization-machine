using System;
using System.Collections.Generic;

namespace Nom.Orch.Services.Support
{
    /// <summary>
    /// The USDA entry a bare pantry name means when a recipe doesn't qualify it ("flour" is
    /// all-purpose wheat flour, not amaranth flour). Names only — every value still comes from the
    /// USDA record — and a staple whose entry isn't in the catalog is simply skipped.
    /// </summary>
    public static class StapleFoods
    {
        private static readonly Dictionary<string, string> ByName = Build(new (string Usda, string[] Names)[]
        {
            ("Wheat flour, white, all-purpose, enriched, bleached", new[] { "flour", "all purpose flour", "all-purpose flour", "plain flour", "white flour", "ap flour" }),
            ("Sugars, granulated", new[] { "sugar", "white sugar", "granulated sugar", "caster sugar", "cane sugar" }),
            ("Sugars, brown", new[] { "brown sugar", "light brown sugar", "dark brown sugar", "packed brown sugar" }),
            ("Sugars, powdered", new[] { "powdered sugar", "icing sugar", "confectioners sugar", "confectioners' sugar" }),
            ("Butter, salted", new[] { "butter", "salted butter" }),
            ("Butter, without salt", new[] { "unsalted butter", "sweet butter" }),
            ("Milk, whole, 3.25% milkfat, with added vitamin D", new[] { "milk", "whole milk" }),
            ("Spices, pepper, black", new[] { "pepper", "black pepper", "ground pepper", "ground black pepper", "freshly ground black pepper" }),
            ("Egg, whole, raw, fresh", new[] { "egg", "eggs", "large egg", "large eggs", "whole egg", "whole eggs" }),
            ("Beverages, water, tap, drinking", new[] { "water", "cold water", "warm water", "hot water", "boiling water" }),
            ("Salt, table", new[] { "salt", "table salt", "fine salt" }),
            ("Oil, olive, salad or cooking", new[] { "olive oil", "extra virgin olive oil", "extra-virgin olive oil" }),
            ("Oil, vegetable, soybean, refined", new[] { "vegetable oil" }),
            ("Oil, canola", new[] { "canola oil", "rapeseed oil" }),
            ("Leavening agents, baking soda", new[] { "baking soda", "bicarbonate of soda" }),
            ("Cream, fluid, heavy whipping", new[] { "heavy cream", "heavy whipping cream", "whipping cream", "double cream" }),
            ("Onions, raw", new[] { "onion", "onions" }),
            ("Garlic, raw", new[] { "garlic", "garlic clove", "garlic cloves", "clove garlic", "cloves garlic" }),
            ("Parsley, fresh", new[] { "parsley", "fresh parsley" }),
            ("Vinegar, distilled", new[] { "vinegar", "white vinegar", "distilled vinegar" }),
            ("Spices, nutmeg, ground", new[] { "nutmeg", "ground nutmeg" }),
            ("Spices, ginger, ground", new[] { "ground ginger" }),
            ("Spices, cumin seed", new[] { "cumin", "ground cumin" }),
            ("Mustard, prepared, yellow", new[] { "mustard", "prepared mustard", "yellow mustard" }),
            ("Cheese, parmesan, grated", new[] { "parmesan", "parmesan cheese", "grated parmesan", "grated parmesan cheese" }),
            ("Tomatoes, red, ripe, raw, year round average", new[] { "tomato", "tomatoes" }),
            ("Potatoes, flesh and skin, raw", new[] { "potato", "potatoes" }),
            ("Carrots, raw", new[] { "carrot", "carrots" }),
            ("Celery, raw", new[] { "celery", "celery stalk", "celery stalks" }),
            ("Rice, white, long-grain, regular, raw, enriched", new[] { "rice", "white rice", "long grain rice", "long-grain rice" }),
            ("Soy sauce made from soy and wheat (shoyu)", new[] { "soy sauce" }),
            ("Beef, ground, 80% lean meat / 20% fat, raw", new[] { "ground beef", "minced beef", "beef mince" }),
        });

        private static Dictionary<string, string> Build((string Usda, string[] Names)[] rows)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (usda, names) in rows)
                foreach (var n in names)
                    map[Key(n)] = usda;
            return map;
        }

        private static string Key(string name) =>
            string.Join(' ', name.ToLowerInvariant().Replace('-', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries));

        /// <summary>The USDA description the name means, or null when it isn't a known bare staple name.</summary>
        public static string? UsdaNameFor(string ingredientName) =>
            ByName.TryGetValue(Key(ingredientName.Trim()), out var usda) ? usda : null;
    }
}

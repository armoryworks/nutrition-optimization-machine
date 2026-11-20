using System;
using System.Collections.Generic;
using System.Linq;
using Nom.Data.Reference;

namespace Nom.Data.Nutrition
{
    /// <summary>
    /// Deterministic keyword classification of a retailer category label (FDC's
    /// branded_food_category) into a <see cref="FoodGroupEnum"/>, and whether a product in it is
    /// eaten as-is. Food names are never classified: "pepper" names black pepper as readily as a
    /// vegetable, and a wrong group is worse than none. USDA Foundation and SR Legacy foods are
    /// grouped by their category id in <see cref="CatalogAdmissionRule"/>.
    /// </summary>
    public static class FoodGroupHeuristics
    {
        // A retailer/product *category* label (e.g. FDC's branded_food_category) is a much more
        // reliable classification signal than a product name — "Nature Valley Protein" tells you
        // little, "Snack, Energy & Granola Bars" tells you a lot. Most specific first.
        private static readonly (string[] Keywords, long Group)[] CategoryKeywords = new[]
        {
            (new[] { "nut", "seed", "trail mix", "peanut butter" }, (long)FoodGroupEnum.NutsSeeds),
            (new[] { "bean", "legume", "lentil", "hummus", "tofu" }, (long)FoodGroupEnum.Legumes),
            (new[] { "milk", "cheese", "yogurt", "cream", "dairy", "butter" }, (long)FoodGroupEnum.Dairy),
            (new[] { "candy", "chocolate", "cookie", "cracker", "chip", "snack", "popcorn", "dessert",
                     "ice cream", "pastr", "cake", "pie ", "sweet", "granola bar", "energy bar" }, (long)FoodGroupEnum.SweetsSnacks),
            (new[] { "beverage", "juice", "soda", "water", "coffee", "tea", "drink" }, (long)FoodGroupEnum.Beverages),
            (new[] { "meat", "poultry", "chicken", "beef", "pork", "seafood", "fish", "sausage",
                     "bacon", "jerky", "egg", "deli" }, (long)FoodGroupEnum.ProteinFoods),
            (new[] { "vegetable", "potato", "salad", "pickle" }, (long)FoodGroupEnum.Vegetables),
            (new[] { "fruit", "berr", "apple", "banana", "citrus", "melon" }, (long)FoodGroupEnum.Fruits),
            (new[] { "bread", "cereal", "pasta", "rice", "grain", "tortilla", "bakery", "flour",
                     "noodle", "oat" }, (long)FoodGroupEnum.Grains),
            (new[] { "oil", "fat", "shortening", "margarine", "mayonnaise", "dressing" }, (long)FoodGroupEnum.FatsOils),
        };

        // Categories that are cooking/baking inputs rather than something eaten as-is.
        private static readonly string[] NotDirectlyEdible =
        {
            "flour", "baking", "sugars & sweeteners", "spice", "seasoning", "oil", "vinegar",
            "shortening", "yeast", "food coloring", "extract", "starch", "cooking",
        };

        // Condiment/sauce categories belong to no food group, and their compound retail names
        // ("Ketchup, Mustard, BBQ & Cheese Sauce") collide with real group keywords. Leaving them
        // unclassified is deliberate: a wrong group is worse than none, because household
        // food-group minimums would count BBQ sauce as a serving of dairy. Precision over recall.
        private static readonly string[] NotAFoodGroup =
        {
            "ketchup", "mustard", "condiment", "sauce", "gravy", "marinade", "dressing",
            "dip", "salsa", "relish", "seasoning", "spice",
        };

        /// <summary>
        /// True when the category positively identifies a condiment/sauce-style product that
        /// belongs to no food group. Callers must treat this as conclusive and NOT fall back to
        /// name keywords — "Prego Sauces Tomato Basil" would otherwise land in Vegetables on the
        /// word "tomato" and count toward a household vegetable minimum.
        /// </summary>
        public static bool IsNonFoodGroupCategory(string? category)
        {
            if (string.IsNullOrWhiteSpace(category)) return false;
            var c = category.ToLowerInvariant();
            if (CategoryKeywords[0].Keywords.Any(k => c.Contains(k))) return false; // nut/seed butters
            return NotAFoodGroup.Any(k => c.Contains(k));
        }

        private static readonly System.Text.RegularExpressions.Regex OilCategory = new(@"\boils?\b",
            System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        /// <summary>
        /// Food group implied by a product category label, or null when the category is
        /// unrecognized or is a condiment-style category that maps to no group. A category that
        /// names oils is Fats/Oils before any other word in it counts ("Vegetable &amp; Cooking Oils").
        /// </summary>
        public static long? ClassifyByCategory(string? category)
        {
            if (string.IsNullOrWhiteSpace(category)) return null;
            var c = category.ToLowerInvariant();

            // Nut/seed butters are genuine Nuts/Seeds even though they read like spreads, so the
            // specific check runs before the condiment guard.
            if (CategoryKeywords[0].Keywords.Any(k => c.Contains(k))) return CategoryKeywords[0].Group;
            if (NotAFoodGroup.Any(k => c.Contains(k))) return null;
            if (OilCategory.IsMatch(c)) return (long)FoodGroupEnum.FatsOils;

            foreach (var (keywords, group) in CategoryKeywords)
                if (keywords.Any(k => c.Contains(k)))
                    return group;
            return null;
        }

        /// <summary>
        /// Whether a product in this category is normally eaten as-is (true) or is a cooking/baking
        /// input (false). Null when there is no category to judge from. Packaged consumer products
        /// default to directly edible, which is the common case.
        /// </summary>
        public static bool? IsDirectlyEdibleCategory(string? category)
        {
            if (string.IsNullOrWhiteSpace(category)) return null;
            var c = category.ToLowerInvariant();
            return !NotDirectlyEdible.Any(k => c.Contains(k));
        }
    }
}

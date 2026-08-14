using System;
using System.Collections.Generic;
using System.Linq;
using Nom.Data.Reference;

namespace Nom.Data.Nutrition
{
    /// <summary>
    /// Deterministic keyword classification of a food name into a <see cref="FoodGroupEnum"/>.
    /// The open-core fallback used both by the nom-api auto-classify endpoint and the Nom.Import
    /// enrichment pass, so there is a single source of truth. Heuristic and admin-overridable;
    /// an optional AI pass can refine it, but this always works with no external dependency.
    /// </summary>
    public static class FoodGroupHeuristics
    {
        // Ordered most-specific-first so "peanut butter" → Nuts/Seeds before Dairy, "chickpea" →
        // Legumes before Vegetables, etc.
        private static readonly (long GroupId, string[] Keywords)[] Keywords = new[]
        {
            ((long)FoodGroupEnum.NutsSeeds, new[] { "almond", "walnut", "peanut", "cashew", "pistachio", "pecan", "hazelnut", "macadamia", "sesame", "sunflower seed", "pumpkin seed", "chia", "flax", " seed" }),
            ((long)FoodGroupEnum.Legumes, new[] { "lentil", "chickpea", "garbanzo", "black bean", "kidney bean", "pinto", "soybean", "soy ", "tofu", "edamame", "hummus", "split pea" }),
            ((long)FoodGroupEnum.Beverages, new[] { "juice", "soda", "cola", "coffee", "tea", "lemonade", "beer", "wine", "smoothie", " drink" }),
            ((long)FoodGroupEnum.SweetsSnacks, new[] { "candy", "chocolate", "cookie", "cake", "brownie", "donut", "doughnut", "chip", "pretzel", "sugar", "syrup", "honey", "jam", "jelly", "ice cream", "protein bar", "granola bar", "candy bar" }),
            ((long)FoodGroupEnum.Dairy, new[] { "milk", "cheese", "yogurt", "cheddar", "mozzarella", "parmesan", "cream ", "sour cream", "butter" }),
            ((long)FoodGroupEnum.ProteinFoods, new[] { "chicken", "beef", "pork", "turkey", "salmon", "tuna", "shrimp", "steak", "bacon", "sausage", " ham", "lamb", "cod", "tilapia", "sardine", " fish", "egg" }),
            ((long)FoodGroupEnum.Fruits, new[] { "apple", "banana", "berry", "strawberr", "blueberr", "raspberr", "orange", "grape", "melon", "mango", "peach", "pear", "pineapple", "plum", "cherry", "kiwi", "watermelon", "lemon", "lime", "apricot", " fig", "raisin", "cranberr", "pomegranate", "avocado" }),
            ((long)FoodGroupEnum.Vegetables, new[] { "lettuce", "spinach", "kale", "broccoli", "carrot", "tomato", "cucumber", "pepper", "onion", "garlic", "potato", "celery", "cabbage", "cauliflower", "zucchini", "squash", "asparagus", "mushroom", " corn", "eggplant", "beet", "radish", "brussels", "green bean" }),
            ((long)FoodGroupEnum.Grains, new[] { " rice", "bread", "pasta", " oat", "wheat", "flour", "cereal", "quinoa", "barley", "tortilla", "cracker", "bagel", "noodle", "couscous", "bulgur" }),
            ((long)FoodGroupEnum.FatsOils, new[] { " oil", "olive oil", "canola", "margarine", "lard", "shortening" }),
        };

        /// <summary>Best-effort food group for a name, or null when no keyword matches.</summary>
        public static long? ClassifyFoodGroup(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            var n = name.ToLowerInvariant();
            foreach (var (groupId, keywords) in Keywords)
                if (keywords.Any(k => n.Contains(k)))
                    return groupId;
            return null;
        }
    }
}

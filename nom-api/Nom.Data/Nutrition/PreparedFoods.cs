using System;
using System.Linq;
using System.Linq.Expressions;
using Nom.Data.Recipe;

namespace Nom.Data.Nutrition
{
    /// <summary>
    /// Prepared foods: the USDA foods of Baby Foods, Fast Foods, Meals, Entrees and Side Dishes, and
    /// Restaurant Foods (<see cref="CatalogAdmissionRule.PreparedCategories"/>), known by their stored
    /// <see cref="IngredientEntity.FdcCategoryId"/>. The admission rule admits them like any other USDA
    /// food so a household can put one in its plan as a food of its own, but they are dishes, not
    /// ingredients: no recipe line is ever linked to one, by a lane, a rule or a person, and the
    /// food-group top-up and the food-group minimums never use one. A branded food of one of the whole-meal categories
    /// of FDC's branded food category (<see cref="BrandedMealCategories"/>, stored in <see cref="IngredientEntity.FdcCategory"/>)
    /// is a prepared meal too; branded data is offered only once the branded lift has curated it.
    /// </summary>
    public static class PreparedFoods
    {
        public const string CategoryName = "Prepared";

        /// <summary>The USDA category ids of prepared foods, for queries.</summary>
        public static readonly int[] CategoryIds = CatalogAdmissionRule.PreparedCategories.Keys.OrderBy(id => id).ToArray();

        /// <summary>
        /// The prepared categories the plan generator draws a prepared meal from: Fast Foods, Meals, Entrees and
        /// Side Dishes, and Restaurant Foods. Baby Foods are prepared foods a household adds by hand; the generator
        /// never serves one as a household's meal.
        /// </summary>
        public static readonly int[] MealCategoryIds = { 21, 22, 25 };

        /// <summary>
        /// The FDC branded food categories whose products are whole meals (frozen dinners and entrees, ready-made
        /// combination meals, prepared subs, sandwiches and wraps, breakfast sandwiches and meals), matched exactly as
        /// FDC spells them. A branded food of one of them is a prepared meal the generator may draw.
        /// </summary>
        public static readonly string[] BrandedMealCategories =
        {
            "Frozen Dinners & Entrees",
            "Ready-Made Combination Meals",
            "Entrees, Sides & Small Meals",
            "Prepared Subs & Sandwiches",
            "Prepared Wraps and Burittos",
            "Sandwiches/Filled Rolls/Wraps",
            "Frozen Breakfast Sandwiches, Biscuits & Meals",
            "Breakfast Sandwiches, Biscuits & Meals",
        };

        /// <summary>Whether a branded food of this stored FDC branded food category is a prepared meal.</summary>
        public static bool IsBrandedMeal(string? dataType, string? category) =>
            dataType == CatalogAdmissionRule.BrandedFood && category != null && BrandedMealCategories.Contains(category);

        /// <summary>Whether a food with this stored USDA category id is a prepared food.</summary>
        public static bool IsPrepared(int? fdcCategoryId) => fdcCategoryId is int id && CatalogAdmissionRule.PreparedCategories.ContainsKey(id);

        /// <summary>Whether a food with this stored USDA category id, FDC data type and stored FDC category is a prepared food, a branded meal included.</summary>
        public static bool IsPrepared(int? fdcCategoryId, string? dataType, string? category) => IsPrepared(fdcCategoryId) || IsBrandedMeal(dataType, category);

        /// <summary>Whether this food is a prepared food.</summary>
        public static bool IsPrepared(IngredientEntity food) => IsPrepared(food.FdcCategoryId, food.FdcDataType, food.FdcCategory);

        /// <summary>The foods that are not prepared foods, as a query filter.</summary>
        public static readonly Expression<Func<IngredientEntity, bool>> NotPrepared =
            i => (i.FdcCategoryId == null || !CategoryIds.Contains(i.FdcCategoryId.Value))
                 && !(i.FdcDataType == CatalogAdmissionRule.BrandedFood && i.FdcCategory != null && BrandedMealCategories.Contains(i.FdcCategory));

        /// <summary>The prepared foods, as a query filter.</summary>
        public static readonly Expression<Func<IngredientEntity, bool>> Prepared =
            i => (i.FdcCategoryId != null && CategoryIds.Contains(i.FdcCategoryId.Value))
                 || (i.FdcDataType == CatalogAdmissionRule.BrandedFood && i.FdcCategory != null && BrandedMealCategories.Contains(i.FdcCategory));

        /// <summary>The prepared foods the plan generator may draw as a meal (<see cref="MealCategoryIds"/> and <see cref="BrandedMealCategories"/>), as a query filter.</summary>
        public static readonly Expression<Func<IngredientEntity, bool>> Meals =
            i => (i.FdcCategoryId != null && MealCategoryIds.Contains(i.FdcCategoryId.Value))
                 || (i.FdcDataType == CatalogAdmissionRule.BrandedFood && i.FdcCategory != null && BrandedMealCategories.Contains(i.FdcCategory));

        /// <summary>The foods of a query that may be a recipe line's food or a food-group food: every food but the prepared ones.</summary>
        public static IQueryable<IngredientEntity> ExceptPrepared(this IQueryable<IngredientEntity> foods) => foods.Where(NotPrepared);
    }
}

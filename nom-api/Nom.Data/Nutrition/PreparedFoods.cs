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
    /// food-group top-up and the food-group minimums never use one.
    /// </summary>
    public static class PreparedFoods
    {
        public const string CategoryName = "Prepared";

        /// <summary>The USDA category ids of prepared foods, for queries.</summary>
        public static readonly int[] CategoryIds = CatalogAdmissionRule.PreparedCategories.Keys.OrderBy(id => id).ToArray();

        /// <summary>Whether a food with this stored USDA category id is a prepared food.</summary>
        public static bool IsPrepared(int? fdcCategoryId) => fdcCategoryId is int id && CatalogAdmissionRule.PreparedCategories.ContainsKey(id);

        /// <summary>Whether this food is a prepared food.</summary>
        public static bool IsPrepared(IngredientEntity food) => IsPrepared(food.FdcCategoryId);

        /// <summary>The foods that are not prepared foods, as a query filter.</summary>
        public static readonly Expression<Func<IngredientEntity, bool>> NotPrepared =
            i => i.FdcCategoryId == null || !CategoryIds.Contains(i.FdcCategoryId.Value);

        /// <summary>The foods of a query that may be a recipe line's food or a food-group food: every food but the prepared ones.</summary>
        public static IQueryable<IngredientEntity> ExceptPrepared(this IQueryable<IngredientEntity> foods) => foods.Where(NotPrepared);
    }
}

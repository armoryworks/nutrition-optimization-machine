// File: Nom.Data/Recipe/RecipeNutritionEntity.cs

using System;
using Nom.Data.Audit;
using Nom.Data.Nutrient;

namespace Nom.Data.Recipe
{
    public class RecipeNutritionEntity : BaseEntity
    {
        public long RecipeId { get; set; }
        public virtual RecipeEntity? Recipe { get; set; }

        public long NutrientId { get; set; }
        public virtual NutrientEntity? Nutrient { get; set; }

        public decimal Amount { get; set; }

        public string? Unit { get; set; }

        public decimal? DailyValuePercentage { get; set; }

        public DateTime? DateCalculated { get; set; }
    }
}

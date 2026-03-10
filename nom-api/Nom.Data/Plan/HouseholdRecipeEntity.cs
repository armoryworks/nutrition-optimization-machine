// File: Nom.Data/Plan/HouseholdRecipeEntity.cs

using Nom.Data.Audit;
using Nom.Data.Plan;
using Nom.Data.Recipe;

namespace Nom.Data.Plan
{
    public class HouseholdRecipeEntity : BaseEntity
    {
        public long HouseholdId { get; set; }
        public virtual HouseholdEntity? Household { get; set; }

        public long RecipeId { get; set; }
        public virtual RecipeEntity? Recipe { get; set; }
    }
}
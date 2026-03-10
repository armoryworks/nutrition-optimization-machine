// File: Nom.Data/Plan/HouseholdCookbookRecipeEntity.cs

using Nom.Data.Audit;
using Nom.Data.Plan;
using Nom.Data.Recipe;

namespace Nom.Data.Plan
{
    public class HouseholdCookbookRecipeEntity : BaseEntity
    {
        public long HouseholdCookbookId { get; set; }
        public virtual HouseholdCookbookEntity? HouseholdCookbook { get; set; }

        public long RecipeId { get; set; }
        public virtual RecipeEntity? Recipe { get; set; }
    }
}
// File: Nom.Data/Plan/HouseholdIngredientEntity.cs

using Nom.Data.Audit;
using Nom.Data.Plan;
using Nom.Data.Recipe;

namespace Nom.Data.Plan
{
    public class HouseholdIngredientEntity : BaseEntity
    {
        public long HouseholdId { get; set; }
        public virtual HouseholdEntity? Household { get; set; }

        public long IngredientId { get; set; }
        public virtual IngredientEntity? Ingredient { get; set; }
    }
}
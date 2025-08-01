// File: Nom.Data/Plan/HouseholdCookbookRecipeEntity.cs

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Nom.Data.Audit;
using Nom.Data.Plan;
using Nom.Data.Recipe;

namespace Nom.Data.Plan
{
    [Table("HouseholdCookbookRecipe", Schema = "plan")]
    public class HouseholdCookbookRecipeEntity : BaseEntity
    {
        [Required]
        public long HouseholdCookbookId { get; set; }
        [ForeignKey(nameof(HouseholdCookbookId))]
        public virtual HouseholdCookbookEntity? HouseholdCookbook { get; set; }

        [Required]
        public long RecipeId { get; set; }
        [ForeignKey(nameof(RecipeId))]
        public virtual RecipeEntity? Recipe { get; set; }
    }
} 
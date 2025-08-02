using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Nom.Data.Person;
using Nom.Data.Plan;
using Nom.Data.Recipe;
using Nom.Data.Shopping;

namespace Nom.Data.Reference
{
    /// <summary>
    /// Represents a category or group for reference items (e.g., "Measurement Units", "Meal Types").
    /// Also supports user/household grouping like Mealie's Group entity.
    /// Maps to the 'Reference.Group' table.
    /// </summary>
    [Table("Group", Schema = "reference")]
    public class GroupEntity : BaseEntity
    {
        [Required]
        public required string Name { get; set; }

        /// <summary>
        /// Optional description for the group. This property maps to the 'Description' column
        /// in the 'reference.Group' table.
        /// </summary>
        public string? Description { get; set; } // Ensure this property exists

        /// <summary>
        /// Slug for URL-friendly group identification (from Mealie)
        /// </summary>
        [MaxLength(255)]
        public string? Slug { get; set; }

        /// <summary>
        /// Navigation property to a collection of ReferenceEntity instances
        /// that belong to this group (many-to-many relationship).
        /// </summary>
        public virtual ICollection<ReferenceEntity>? References { get; set; }

        // User/Household grouping functionality (from Mealie)
        public virtual ICollection<PersonEntity> Members { get; set; } = new List<PersonEntity>();
        public virtual ICollection<RecipeEntity> Recipes { get; set; } = new List<RecipeEntity>();
        public virtual ICollection<HouseholdEntity> Households { get; set; } = new List<HouseholdEntity>();
        public virtual ICollection<MealEntity> MealPlans { get; set; } = new List<MealEntity>();
        public virtual ICollection<ShoppingListEntity> ShoppingLists { get; set; } = new List<ShoppingListEntity>();
    }
}
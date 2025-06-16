using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Nom.Data.Reference; // For MealType
using Nom.Data.Recipe;   // For RecipeEntity relationship
using Nom.Data.Shopping; // NEW: For ShoppingTripEntity relationship

namespace Nom.Data.Plan
{
    /// <summary>
    /// Represents a specific meal within a plan.
    /// Maps to the 'Plan.meal' table.
    /// </summary>
    [Table("Meal", Schema = "plan")] // Table name capitalized, schema lowercase
    public class MealEntity : BaseEntity
    {
        [Required]
        public long PlanId { get; set; }
        [ForeignKey(nameof(PlanId))]
        public virtual PlanEntity Plan { get; set; } = default!;

        [Required]
        public long MealTypeId { get; set; }
        [ForeignKey(nameof(MealTypeId))]
        public virtual ReferenceEntity MealType { get; set; } = default!; // e.g., Breakfast, Lunch, Dinner

        [Required]
        [Column("date", TypeName = "date")] // [date] is a reserved keyword in some DBs, use Column for explicit mapping
        public DateOnly Date { get; set; } // DATE NOT NULL in SQL maps to DateOnly

        // Implicit Many-to-Many relationship with RecipeEntity for recipes (meal_recipe_index)
        public virtual ICollection<RecipeEntity>? Recipes { get; set; }

        // NEW: Implicit Many-to-Many relationship with ShoppingTripEntity
        // This indicates which shopping trips provided ingredients for this meal.
        public virtual ICollection<Shopping.ShoppingTripEntity>? ShoppingTrips { get; set; }
    }
}
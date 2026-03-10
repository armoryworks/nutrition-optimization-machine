using System;
using System.Collections.Generic;
using Nom.Data.Reference; // For MealType
using Nom.Data.Recipe;   // For RecipeEntity relationship
using Nom.Data.Shopping; // NEW: For ShoppingTripEntity relationship

namespace Nom.Data.Plan
{
    /// <summary>
    /// Represents a specific meal within a plan.
    /// Maps to the 'Plan.meal' table.
    /// </summary>
    public class MealEntity : BaseEntity
    {
        public long PlanId { get; set; }
        public virtual PlanEntity Plan { get; set; } = default!;

        public long MealTypeId { get; set; }
        public virtual ReferenceEntity MealType { get; set; } = default!; // e.g., Breakfast, Lunch, Dinner

        public DateOnly Date { get; set; } // DATE NOT NULL in SQL maps to DateOnly

        // Implicit Many-to-Many relationship with RecipeEntity for recipes (meal_recipe_index)
        public virtual ICollection<RecipeEntity>? Recipes { get; set; }

        // NEW: Implicit Many-to-Many relationship with ShoppingTripEntity
        // This indicates which shopping trips provided ingredients for this meal.
        public virtual ICollection<Shopping.ShoppingTripEntity>? ShoppingTrips { get; set; }
    }
}
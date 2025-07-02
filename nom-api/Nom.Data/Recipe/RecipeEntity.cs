// Nom.Data/Recipe/RecipeEntity.cs
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Nom.Data.Reference;
using Nom.Data.Person;
using Nom.Data.Plan;
using Nom.Data.Audit; // Assuming BaseEntity is in Nom.Data.Audit namespace

namespace Nom.Data.Recipe
{
    [Table("Recipe", Schema = "recipe")]
    public class RecipeEntity : BaseEntity
    {
        [Required]
        [MaxLength(511)] // Retaining 511 from previous fix for longer names
        public string Name { get; set; } = string.Empty;

        [MaxLength(2047)]
        public string? Description { get; set; }

        [Column(TypeName = "text")] // Explicitly map to TEXT for potentially very long JSON strings
        public string? Instructions { get; set; }

        // Fields for preparation and cooking times (in minutes)
        public int? PrepTimeMinutes { get; set; }
        public int? CookTimeMinutes { get; set; }

        // New property for the total number of servings the recipe yields
        public int? Servings { get; set; } // e.g., 8 (for 8 individual servings)

        // These fields now represent the quantity and measurement type FOR A SINGLE SERVING
        // Example: If a recipe yields 8 servings, and each serving is 1 cup, then:
        // Servings = 8, ServingQuantity = 1.0, ServingQuantityMeasurementType = "cup"
        [Column(TypeName = "decimal(18,2)")] // Ensure proper decimal mapping
        public decimal? ServingQuantity { get; set; }

        public long? ServingQuantityMeasurementTypeId { get; set; } // Reference to MeasurementType for a single serving (e.g., "cup", "grams")
        [ForeignKey(nameof(ServingQuantityMeasurementTypeId))]
        public virtual ReferenceEntity? ServingQuantityMeasurementType { get; set; }

        // This field will store the raw, unparsed ingredient string (JSON array from CSV).
        [Column(TypeName = "text")] // Explicitly map to TEXT for potentially very long JSON strings
        public string? RawIngredientsString { get; set; }

        [Required]
        public bool IsCurated { get; set; } = false;

        public long? CuratedById { get; set; }
        [ForeignKey(nameof(CuratedById))]
        public virtual PersonEntity? Curator { get; set; } // Inverse of PersonEntity.CuratedRecipes

        [Column(TypeName = "date")]
        public DateOnly? CuratedDate { get; set; }

        [MaxLength(2047)] // URL from the source (e.g., cookbooks.com link)
        public string? SourceUrl { get; set; }

        [MaxLength(255)] // Name of the source site (e.g., "cookbooks.com", "Gathered")
        public string? SourceSite { get; set; }

        // Navigation properties for related entities (keeping these for granular data)
        public virtual ICollection<RecipeIngredientEntity>? RecipeIngredients { get; set; } // Renamed from Ingredients to match existing pattern
        public virtual ICollection<RecipeStepEntity>? RecipeSteps { get; set; } // Renamed from Steps to match existing pattern
        public virtual ICollection<ReferenceEntity>? RecipeTypes { get; set; }
        public virtual ICollection<MealEntity>? Meals { get; set; }
    }
}

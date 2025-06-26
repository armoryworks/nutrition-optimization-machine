using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Nom.Data.Reference;

namespace Nom.Data.Nutrient
{
    /// <summary>
    /// Represents a specific nutrient (e.g., "Calories", "Protein", "Vitamin C").
    /// Maps to the 'Nutrient.nutrient' table.
    /// </summary>
    [Table("Nutrient", Schema = "nutrient")] // Adjusted: Table name capitalized, schema lowercase
    public class NutrientEntity : BaseEntity
    {
        [Required]
        [MaxLength(255)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(2047)]
        public string? Description { get; set; }

        public long? DefaultMeasurementTypeId { get; set; } // Nullable if not all nutrients have a default
        [ForeignKey(nameof(DefaultMeasurementTypeId))]
        public virtual ReferenceEntity? DefaultMeasurementType { get; set; } // Navigation property to ReferenceEntity

        // Reverse navigation properties for NutrientComponentEntity
        // These collections represent the "many" side of the one-to-many relationships
        // defined by MacroNutrient and MicroNutrient in NutrientComponentEntity.

        /// <summary>
        /// Collection of NutrientComponentEntity where this Nutrient is the MacroNutrient.
        /// </summary>
        public virtual ICollection<NutrientComponentEntity>? MacroComponents { get; set; }

        /// <summary>
        /// Collection of NutrientComponentEntity where this Nutrient is the MicroNutrient.
        /// </summary>
        public virtual ICollection<NutrientComponentEntity>? MicroComponents { get; set; }


        // Other navigation properties (e.g., to NutrientGuideline, IngredientNutrient)
        public virtual ICollection<NutrientGuidelineEntity>? Guidelines { get; set; }

        public virtual ICollection<Recipe.IngredientNutrientEntity>? IngredientNutrients { get; set; }
    }
}
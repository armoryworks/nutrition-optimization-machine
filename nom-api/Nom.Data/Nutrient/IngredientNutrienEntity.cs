// Nom.Data/Nutrient/IngredientNutrientEntity.cs
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Nom.Data.Recipe; // Required for Ingredient navigation property
using Nom.Data.Reference; // Required for MeasurementType navigation property

namespace Nom.Data.Nutrient
{
    /// <summary>
    /// Represents the nutritional content of a specific ingredient (e.g., protein in chicken breast).
    /// This entity links an Ingredient to a Nutrient and specifies the amount per typical serving/100g.
    /// Maps to the 'Nutrient.ingredient_nutrient' table.
    /// </summary>
    [Table("IngredientNutrient", Schema = "nutrient")] // Table name capitalized, schema lowercase
    public class IngredientNutrientEntity : BaseEntity
    {
        /// <summary>
        /// Foreign key to the Recipe.Ingredient table, identifying the ingredient.
        /// Corresponds to BIGINT NOT NULL.
        /// </summary>
        public long IngredientId { get; set; }

        /// <summary>
        /// Navigation property to the associated IngredientEntity.
        /// </summary>
        [ForeignKey(nameof(IngredientId))]
        public virtual IngredientEntity Ingredient { get; set; } = default!;

        /// <summary>
        /// Foreign key to the Nutrient.Nutrient table, identifying the nutrient.
        /// Corresponds to BIGINT NOT NULL.
        /// </summary>
        public long NutrientId { get; set; }

        /// <summary>
        /// Navigation property to the associated NutrientEntity.
        /// </summary>
        [ForeignKey(nameof(NutrientId))]
        public virtual NutrientEntity Nutrient { get; set; } = default!;

        /// <summary>
        /// The amount of the nutrient present in the ingredient (per 100g by default, or per serving).
        /// Corresponds to DECIMAL NOT NULL.
        /// </summary>
        [Required]
        [Column(TypeName = "decimal(18,4)")] // Using a common decimal precision/scale, adjust if needed
        public decimal Amount { get; set; }

        /// <summary>
        /// Foreign key to the Reference.Reference table, indicating the unit of measurement
        /// for the amount (e.g., "mg", "g", "kcal").
        /// </summary>
        public long MeasurementTypeId { get; set; }

        /// <summary>
        /// Navigation property to the associated MeasurementType ReferenceEntity.
        /// </summary>
        [ForeignKey(nameof(MeasurementTypeId))]
        public virtual ReferenceEntity? MeasurementType { get; set; } // Nullable if 'unknown' is allowed for type 0

        /// <summary>
        /// The FoodData Central (FDC) ID for the specific nutrient, if this data originated from FDC.
        /// Useful for traceability and debugging. Nullable if not from FDC.
        /// </summary>
        [MaxLength(50)] // Arbitrary max length, adjust if FDC IDs are longer
        public string? FdcId { get; set; }
    }
}

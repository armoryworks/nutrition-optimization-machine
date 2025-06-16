using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Nom.Data.Reference; // For MeasurementType
using Nom.Data.Nutrient; // For NutrientEntity

namespace Nom.Data.Recipe
{
    [Table("IngredientNutrient", Schema = "recipe")] // Adjusted: Table name PascalCase, schema lowercase
    public class IngredientNutrientEntity : BaseEntity
    {
        [Required]
        public long IngredientId { get; set; } // This is assumed to reference Recipe.ingredient(id)
        [ForeignKey(nameof(IngredientId))]
        public virtual IngredientEntity Ingredient { get; set; } = default!; // Navigation to the ingredient it belongs to

        [Required]
        public long NutrientId { get; set; } // This will reference Nutrient.nutrient(id)
        [ForeignKey(nameof(NutrientId))]
        public virtual NutrientEntity Nutrient { get; set; } = default!; // Navigation to the specific nutrient (e.g., Protein, Vitamin C)

        [Required]
        public long MeasurementTypeId { get; set; }
        [ForeignKey(nameof(MeasurementTypeId))]
        public virtual ReferenceEntity MeasurementType { get; set; } = default!; // Measurement unit for this nutrient (e.g., mg, g)

        [Required]
        [Column(TypeName = "decimal(18,2)")] // Ensure proper decimal mapping
        public decimal Measurement { get; set; }
    }
}
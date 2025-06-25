using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Nom.Data.Reference; // For MeasurementType

namespace Nom.Data.Recipe
{
    [Table("RecipeIngredient", Schema = "recipe")] // Adjusted: Table name PascalCase, schema lowercase
    public class RecipeIngredientEntity : BaseEntity
    {
        [Required]
        public long RecipeId { get; set; }
        [ForeignKey(nameof(RecipeId))]
        public virtual RecipeEntity Recipe { get; set; } = default!;

        [Required]
        public long IngredientId { get; set; }
        [ForeignKey(nameof(IngredientId))]
        public virtual IngredientEntity Ingredient { get; set; } = default!;

        [Required]
        public long MeasurementTypeId { get; set; }
        [ForeignKey(nameof(MeasurementTypeId))]
        public virtual ReferenceEntity MeasurementType { get; set; } = default!;

        [Required]
        [Column(TypeName = "decimal(18,2)")] // Ensure proper decimal mapping
        public decimal Measurement { get; set; }

        [Column(TypeName = "varchar(4000)")]
        public string? OriginalText { get; set; } // Optional field for original text from recipe source
    }
}
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Nom.Data.Recipe;

namespace Nom.Data.Measurement
{
    /// <summary>
    /// Represents ingredient-specific measurement preferences and typical quantities.
    /// Maps to the 'measurement.IngredientMeasurement' table.
    /// </summary>
    [Table("IngredientMeasurement", Schema = "measurement")]
    public class IngredientMeasurementEntity : MeasurementEntity
    {
        [Required]
        public long IngredientId { get; set; }

        [ForeignKey(nameof(IngredientId))]
        public virtual IngredientEntity Ingredient { get; set; } = default!;

        [Column(TypeName = "decimal(18,4)")]
        public decimal? TypicalQuantity { get; set; }

        public bool IsPreferredUnit { get; set; } = false;

        public long? PreferredMeasurementId { get; set; }

        [ForeignKey(nameof(PreferredMeasurementId))]
        public virtual MeasurementEntity? PreferredMeasurement { get; set; }

        public long? DefaultMeasurementId { get; set; }

        [ForeignKey(nameof(DefaultMeasurementId))]
        public virtual MeasurementEntity? DefaultMeasurement { get; set; }

        public bool IsPreferred { get; set; } = false;

        [MaxLength(500)]
        public string? Notes { get; set; }
    }
}

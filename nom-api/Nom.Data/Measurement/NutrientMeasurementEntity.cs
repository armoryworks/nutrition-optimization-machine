using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Nom.Data.Nutrient;

namespace Nom.Data.Measurement
{
    /// <summary>
    /// Represents nutrient-specific measurement standards and typical amounts.
    /// Maps to the 'measurement.NutrientMeasurement' table.
    /// </summary>
    [Table("NutrientMeasurement", Schema = "measurement")]
    public class NutrientMeasurementEntity : MeasurementEntity
    {
        [Required]
        public long NutrientId { get; set; }

        [ForeignKey(nameof(NutrientId))]
        public virtual NutrientEntity Nutrient { get; set; } = default!;

        [Column(TypeName = "decimal(18,4)")]
        public decimal? StandardAmount { get; set; }

        public bool IsStandardUnit { get; set; } = false;
    }
}

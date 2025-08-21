using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Nom.Data.Audit;

namespace Nom.Data.Measurement
{
    /// <summary>
    /// Represents conversion rules between different measurement units.
    /// Maps to the 'measurement.MeasurementConversion' table.
    /// </summary>
    [Table("MeasurementConversion", Schema = "measurement")]
    public class MeasurementConversionEntity : BaseEntity
    {
        [Required]
        public long FromMeasurementId { get; set; }

        [ForeignKey(nameof(FromMeasurementId))]
        public virtual MeasurementEntity FromMeasurement { get; set; } = default!;

        [Required]
        public long ToMeasurementId { get; set; }

        [ForeignKey(nameof(ToMeasurementId))]
        public virtual MeasurementEntity ToMeasurement { get; set; } = default!;

        [Required]
        [Column(TypeName = "decimal(18,6)")]
        public decimal ConversionFactor { get; set; }

        [Column(TypeName = "decimal(18,6)")]
        public decimal? Offset { get; set; }

        [MaxLength(100)]
        public string? Formula { get; set; }

        [Required]
        public bool IsDirectConversion { get; set; } = true;
    }
}

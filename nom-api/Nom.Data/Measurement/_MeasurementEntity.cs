using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Nom.Data.Audit;

namespace Nom.Data.Measurement
{
    /// <summary>
    /// Abstract base class for all measurement entities. Implements Table-Per-Hierarchy (TPH).
    /// Maps to the 'measurement.Measurement' table.
    /// </summary>
    public abstract class MeasurementEntity : BaseEntity
    {
        [Required]
        [MaxLength(100)]
        public required string Name { get; set; }

        [MaxLength(500)]
        public string? Description { get; set; }

        [Required]
        [MaxLength(20)]
        public required string Symbol { get; set; }

        [Required]
        public long MeasurementCategoryId { get; set; }

        [ForeignKey(nameof(MeasurementCategoryId))]
        public virtual MeasurementCategoryEntity Category { get; set; } = default!;

        [Required]
        public bool IsBaseUnit { get; set; } = false;

        [Column(TypeName = "decimal(18,6)")]
        public decimal? BaseUnitConversionFactor { get; set; }
    }
}

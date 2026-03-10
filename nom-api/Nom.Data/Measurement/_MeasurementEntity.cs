using Nom.Data.Audit;

namespace Nom.Data.Measurement
{
    /// <summary>
    /// Abstract base class for all measurement entities. Implements Table-Per-Hierarchy (TPH).
    /// Maps to the 'measurement.Measurement' table.
    /// </summary>
    public abstract class MeasurementEntity : BaseEntity
    {
        public required string Name { get; set; }

        public string? Description { get; set; }

        public required string Symbol { get; set; }

        public long MeasurementCategoryId { get; set; }

        public virtual MeasurementCategoryEntity Category { get; set; } = default!;

        public bool IsBaseUnit { get; set; } = false;

        public decimal? BaseUnitConversionFactor { get; set; }
    }
}

using Nom.Data.Audit;

namespace Nom.Data.Measurement
{
    /// <summary>
    /// Represents conversion rules between different measurement units.
    /// Maps to the 'measurement.MeasurementConversion' table.
    /// </summary>
    public class MeasurementConversionEntity : BaseEntity
    {
        public long FromMeasurementId { get; set; }

        public virtual MeasurementEntity FromMeasurement { get; set; } = default!;

        public long ToMeasurementId { get; set; }

        public virtual MeasurementEntity ToMeasurement { get; set; } = default!;

        public decimal ConversionFactor { get; set; }

        public decimal? Offset { get; set; }

        public string? Formula { get; set; }

        public bool IsDirectConversion { get; set; } = true;
    }
}

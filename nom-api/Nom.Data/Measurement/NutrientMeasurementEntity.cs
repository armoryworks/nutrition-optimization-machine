using Nom.Data.Nutrient;

namespace Nom.Data.Measurement
{
    /// <summary>
    /// Represents nutrient-specific measurement standards and typical amounts.
    /// Maps to the 'measurement.NutrientMeasurement' table.
    /// </summary>
    public class NutrientMeasurementEntity : MeasurementEntity
    {
        public long NutrientId { get; set; }

        public virtual NutrientEntity Nutrient { get; set; } = default!;

        public decimal? StandardAmount { get; set; }

        public bool IsStandardUnit { get; set; } = false;

        public long? StandardMeasurementId { get; set; }

        public virtual MeasurementEntity? StandardMeasurement { get; set; }

        public long? DefaultMeasurementId { get; set; }

        public virtual MeasurementEntity? DefaultMeasurement { get; set; }

        public decimal? StandardDailyValue { get; set; }

        public string? StandardDailyValueUnit { get; set; }

        public string? Notes { get; set; }
    }
}

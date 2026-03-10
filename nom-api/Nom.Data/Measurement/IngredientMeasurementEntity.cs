using Nom.Data.Recipe;

namespace Nom.Data.Measurement
{
    /// <summary>
    /// Represents ingredient-specific measurement preferences and typical quantities.
    /// Maps to the 'measurement.IngredientMeasurement' table.
    /// </summary>
    public class IngredientMeasurementEntity : MeasurementEntity
    {
        public long IngredientId { get; set; }

        public virtual IngredientEntity Ingredient { get; set; } = default!;

        public decimal? TypicalQuantity { get; set; }

        public bool IsPreferredUnit { get; set; } = false;

        public long? PreferredMeasurementId { get; set; }

        public virtual MeasurementEntity? PreferredMeasurement { get; set; }

        public long? DefaultMeasurementId { get; set; }

        public virtual MeasurementEntity? DefaultMeasurement { get; set; }

        public bool IsPreferred { get; set; } = false;

        public string? Notes { get; set; }
    }
}

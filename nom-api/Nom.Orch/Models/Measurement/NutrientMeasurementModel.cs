namespace Nom.Orch.Models.Measurement
{
    /// <summary>
    /// Model representing a nutrient-specific measurement in the API layer.
    /// </summary>
    public class NutrientMeasurementModel
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string Symbol { get; set; } = string.Empty;
        public long CategoryId { get; set; }
        public string CategoryName { get; set; } = string.Empty;
        public bool IsBaseUnit { get; set; }
        public decimal? BaseUnitConversionFactor { get; set; }
        public long NutrientId { get; set; }
        public string NutrientName { get; set; } = string.Empty;
        public decimal? StandardAmount { get; set; }
        public bool IsStandardUnit { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime? LastModifiedDate { get; set; }
    }
}

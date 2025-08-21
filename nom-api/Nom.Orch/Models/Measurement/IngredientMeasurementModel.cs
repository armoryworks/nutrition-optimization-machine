namespace Nom.Orch.Models.Measurement
{
    /// <summary>
    /// Model representing an ingredient-specific measurement in the API layer.
    /// </summary>
    public class IngredientMeasurementModel
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string Symbol { get; set; } = string.Empty;
        public long CategoryId { get; set; }
        public string CategoryName { get; set; } = string.Empty;
        public bool IsBaseUnit { get; set; }
        public decimal? BaseUnitConversionFactor { get; set; }
        public long IngredientId { get; set; }
        public string IngredientName { get; set; } = string.Empty;
        public decimal? TypicalQuantity { get; set; }
        public bool IsPreferredUnit { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime? LastModifiedDate { get; set; }
    }
}

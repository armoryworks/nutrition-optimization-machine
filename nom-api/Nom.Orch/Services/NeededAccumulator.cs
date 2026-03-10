namespace Nom.Orch.Services
{
    internal class NeededAccumulator
    {
        public long IngredientId { get; set; }
        public string IngredientName { get; set; } = string.Empty;
        public string CategoryName { get; set; } = string.Empty;
        public decimal BaseQuantity { get; set; }
        public long MeasurementId { get; set; }
        public string MeasurementName { get; set; } = string.Empty;
        public string MeasurementSymbol { get; set; } = string.Empty;
        public decimal ConversionFactor { get; set; }
    }
}

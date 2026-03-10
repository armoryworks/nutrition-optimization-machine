namespace Nom.Orch.Models.Pantry
{
    /// <summary>
    /// Represents an ingredient needed for upcoming meals minus what's in the pantry.
    /// </summary>
    public class ShoppingNeedModel
    {
        public long IngredientId { get; set; }
        public string IngredientName { get; set; } = string.Empty;
        public decimal QuantityNeeded { get; set; }
        public decimal QuantityOnHand { get; set; }
        public decimal QuantityToBuy { get; set; }
        public long MeasurementId { get; set; }
        public string MeasurementName { get; set; } = string.Empty;
        public string MeasurementSymbol { get; set; } = string.Empty;
        public string MeasurementCategory { get; set; } = string.Empty;
    }
}

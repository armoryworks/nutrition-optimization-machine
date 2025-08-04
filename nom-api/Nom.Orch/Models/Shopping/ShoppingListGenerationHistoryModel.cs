namespace Nom.Orch.Models.Shopping
{
    /// <summary>
    /// Model for shopping list generation history
    /// </summary>
    public class ShoppingListGenerationHistoryModel
    {
        public long Id { get; set; }
        public long ShoppingListId { get; set; }
        public string GenerationMethod { get; set; } = string.Empty;
        public string RequestData { get; set; } = string.Empty;
        public string ResponseData { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime GeneratedDate { get; set; }
        public long GeneratedByUserId { get; set; }
        public decimal ProcessingTime { get; set; }
    }
} 
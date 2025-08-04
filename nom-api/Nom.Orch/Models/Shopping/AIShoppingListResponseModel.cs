namespace Nom.Orch.Models.Shopping
{
    /// <summary>
    /// Model for AI shopping list generation response
    /// </summary>
    public class AIShoppingListResponseModel
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public SmartShoppingListResponseModel? ShoppingList { get; set; }
        public List<ShoppingListSuggestionModel> Suggestions { get; set; } = new();
        public List<string> Errors { get; set; } = new();
        public string? AIReasoning { get; set; }
    }
} 
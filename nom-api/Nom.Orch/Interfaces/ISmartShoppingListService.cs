using Nom.Orch.Models.Shopping;

namespace Nom.Orch.Interfaces
{
    /// <summary>
    /// Service interface for smart shopping list functionality
    /// </summary>
    public interface ISmartShoppingListService
    {
        /// <summary>
        /// Generate smart shopping list from recipes and meal plans
        /// </summary>
        Task<SmartShoppingListResponseModel> GenerateSmartShoppingListAsync(SmartShoppingListRequestModel request);

        /// <summary>
        /// Generate shopping list using AI
        /// </summary>
        Task<AIShoppingListResponseModel> GenerateAIShoppingListAsync(AIShoppingListRequestModel request);

        /// <summary>
        /// Optimize existing shopping list
        /// </summary>
        Task<SmartShoppingListResponseModel> OptimizeShoppingListAsync(ShoppingListOptimizationModel request);

        /// <summary>
        /// Get shopping list suggestions
        /// </summary>
        Task<List<ShoppingListSuggestionModel>> GetShoppingListSuggestionsAsync(long shoppingListId);

        /// <summary>
        /// Get shopping list analytics
        /// </summary>
        Task<ShoppingListAnalyticsModel> GetShoppingListAnalyticsAsync(long shoppingListId);

        /// <summary>
        /// Get shopping list templates
        /// </summary>
        Task<List<ShoppingListTemplateModel>> GetShoppingListTemplatesAsync();

        /// <summary>
        /// Create shopping list template
        /// </summary>
        Task<ShoppingListTemplateModel> CreateShoppingListTemplateAsync(ShoppingListTemplateModel request);

        /// <summary>
        /// Get shopping list generation history
        /// </summary>
        Task<List<ShoppingListGenerationHistoryModel>> GetGenerationHistoryAsync(long shoppingListId);

        /// <summary>
        /// Merge shopping list items intelligently
        /// </summary>
        Task<List<SmartShoppingListItemModel>> MergeShoppingListItemsAsync(List<SmartShoppingListItemModel> items);

        /// <summary>
        /// Suggest substitutions for shopping list items
        /// </summary>
        Task<List<ShoppingListSuggestionModel>> SuggestSubstitutionsAsync(List<SmartShoppingListItemModel> items);

        /// <summary>
        /// Estimate shopping list cost
        /// </summary>
        Task<decimal> EstimateShoppingListCostAsync(List<SmartShoppingListItemModel> items);

        /// <summary>
        /// Get nutritional analysis for shopping list
        /// </summary>
        Task<Dictionary<string, object>> GetNutritionalAnalysisAsync(List<SmartShoppingListItemModel> items);
    }
} 
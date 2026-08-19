using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nom.Api.Filters;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Shopping;

namespace Nom.Api.Controllers
{
    /// <summary>
    /// Quarantined: the service behind this layer still returns placeholder prices and
    /// nutrition (a fixed dictionary + $5 default) and has no UI consumer. Off unless
    /// Features:SmartShoppingList=true, so nothing fabricated is served by default.
    /// </summary>
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    [FeatureGate("SmartShoppingList")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public class SmartShoppingListController : BaseApiController
    {
        private readonly ISmartShoppingListService _smartShoppingListService;
        private readonly IShoppingListOrchestrationService _shoppingListService;

        public SmartShoppingListController(
            ISmartShoppingListService smartShoppingListService,
            IShoppingListOrchestrationService shoppingListService)
        {
            _smartShoppingListService = smartShoppingListService;
            _shoppingListService = shoppingListService;
        }

        /// <summary>403 unless the caller may access shopping list <paramref name="listId"/>.</summary>
        private async Task<bool> CanAccessListAsync(long listId) =>
            await _shoppingListService.CanAccessListByIdAsync(listId, GetCurrentPersonIdRequired());

        [HttpPost("generate")]
        [ProducesResponseType(typeof(SmartShoppingListResponseModel), StatusCodes.Status200OK)]
        public async Task<IActionResult> GenerateSmartShoppingList([FromBody] SmartShoppingListRequestModel request)
        {
            if (!IsHouseholdMember(request.HouseholdId)) return Forbid();
            var result = await _smartShoppingListService.GenerateSmartShoppingListAsync(request);
            return Ok(result);
        }

        [HttpPost("ai-generate")]
        [ProducesResponseType(typeof(AIShoppingListResponseModel), StatusCodes.Status200OK)]
        public async Task<IActionResult> GenerateAIShoppingList([FromBody] AIShoppingListRequestModel request)
        {
            var result = await _smartShoppingListService.GenerateAIShoppingListAsync(request);
            return Ok(result);
        }

        [HttpPost("optimize")]
        [ProducesResponseType(typeof(SmartShoppingListResponseModel), StatusCodes.Status200OK)]
        public async Task<IActionResult> OptimizeShoppingList([FromBody] ShoppingListOptimizationModel request)
        {
            if (!await CanAccessListAsync(request.ShoppingListId)) return Forbid();
            var result = await _smartShoppingListService.OptimizeShoppingListAsync(request);
            return Ok(result);
        }

        [HttpGet("{id}/suggestions")]
        [ProducesResponseType(typeof(List<ShoppingListSuggestionModel>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetSuggestions(long id)
        {
            if (!await CanAccessListAsync(id)) return Forbid();
            var result = await _smartShoppingListService.GetShoppingListSuggestionsAsync(id);
            return Ok(result);
        }

        [HttpGet("{id}/analytics")]
        [ProducesResponseType(typeof(ShoppingListAnalyticsModel), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetAnalytics(long id)
        {
            if (!await CanAccessListAsync(id)) return Forbid();
            var result = await _smartShoppingListService.GetShoppingListAnalyticsAsync(id);
            return Ok(result);
        }

        [HttpGet("templates")]
        [ProducesResponseType(typeof(List<ShoppingListTemplateModel>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetTemplates()
        {
            var result = await _smartShoppingListService.GetShoppingListTemplatesAsync();
            return Ok(result);
        }

        [HttpPost("templates")]
        [ProducesResponseType(typeof(ShoppingListTemplateModel), StatusCodes.Status200OK)]
        public async Task<IActionResult> CreateTemplate([FromBody] ShoppingListTemplateModel request)
        {
            var result = await _smartShoppingListService.CreateShoppingListTemplateAsync(request);
            return Ok(result);
        }

        [HttpGet("{id}/history")]
        [ProducesResponseType(typeof(List<ShoppingListGenerationHistoryModel>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetGenerationHistory(long id)
        {
            if (!await CanAccessListAsync(id)) return Forbid();
            var result = await _smartShoppingListService.GetGenerationHistoryAsync(id);
            return Ok(result);
        }

        [HttpPost("merge-items")]
        [ProducesResponseType(typeof(List<SmartShoppingListItemModel>), StatusCodes.Status200OK)]
        public async Task<IActionResult> MergeItems([FromBody] List<SmartShoppingListItemModel> items)
        {
            var result = await _smartShoppingListService.MergeShoppingListItemsAsync(items);
            return Ok(result);
        }

        [HttpPost("substitutions")]
        [ProducesResponseType(typeof(List<ShoppingListSuggestionModel>), StatusCodes.Status200OK)]
        public async Task<IActionResult> SuggestSubstitutions([FromBody] List<SmartShoppingListItemModel> items)
        {
            var result = await _smartShoppingListService.SuggestSubstitutionsAsync(items);
            return Ok(result);
        }

        [HttpPost("estimate-cost")]
        [ProducesResponseType(typeof(decimal), StatusCodes.Status200OK)]
        public async Task<IActionResult> EstimateCost([FromBody] List<SmartShoppingListItemModel> items)
        {
            var result = await _smartShoppingListService.EstimateShoppingListCostAsync(items);
            return Ok(result);
        }

        [HttpPost("nutritional-analysis")]
        [ProducesResponseType(typeof(Dictionary<string, object>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetNutritionalAnalysis([FromBody] List<SmartShoppingListItemModel> items)
        {
            var result = await _smartShoppingListService.GetNutritionalAnalysisAsync(items);
            return Ok(result);
        }
    }
}

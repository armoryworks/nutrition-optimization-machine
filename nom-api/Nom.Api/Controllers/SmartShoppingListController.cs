using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Shopping;

namespace Nom.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class SmartShoppingListController : BaseApiController
    {
        private readonly ISmartShoppingListService _smartShoppingListService;

        public SmartShoppingListController(
            ISmartShoppingListService smartShoppingListService)
        {
            _smartShoppingListService = smartShoppingListService;
        }

        [HttpPost("generate")]
        [ProducesResponseType(typeof(SmartShoppingListResponseModel), StatusCodes.Status200OK)]
        public async Task<IActionResult> GenerateSmartShoppingList([FromBody] SmartShoppingListRequestModel request)
        {
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
            var result = await _smartShoppingListService.OptimizeShoppingListAsync(request);
            return Ok(result);
        }

        [HttpGet("{id}/suggestions")]
        [ProducesResponseType(typeof(List<ShoppingListSuggestionModel>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetSuggestions(long id)
        {
            var result = await _smartShoppingListService.GetShoppingListSuggestionsAsync(id);
            return Ok(result);
        }

        [HttpGet("{id}/analytics")]
        [ProducesResponseType(typeof(ShoppingListAnalyticsModel), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetAnalytics(long id)
        {
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

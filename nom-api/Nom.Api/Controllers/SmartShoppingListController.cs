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
        private readonly ILogger<SmartShoppingListController> _logger;

        public SmartShoppingListController(
            ISmartShoppingListService smartShoppingListService,
            ILogger<SmartShoppingListController> logger)
        {
            _smartShoppingListService = smartShoppingListService;
            _logger = logger;
        }

        [HttpPost("generate")]
        [ProducesResponseType(typeof(SmartShoppingListResponseModel), StatusCodes.Status200OK)]
        public async Task<IActionResult> GenerateSmartShoppingList([FromBody] SmartShoppingListRequestModel request)
        {
            try
            {
                var result = await _smartShoppingListService.GenerateSmartShoppingListAsync(request);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating smart shopping list");
                return StatusCode(StatusCodes.Status500InternalServerError, "An internal error occurred.");
            }
        }

        [HttpPost("ai-generate")]
        [ProducesResponseType(typeof(AIShoppingListResponseModel), StatusCodes.Status200OK)]
        public async Task<IActionResult> GenerateAIShoppingList([FromBody] AIShoppingListRequestModel request)
        {
            try
            {
                var result = await _smartShoppingListService.GenerateAIShoppingListAsync(request);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating AI shopping list");
                return StatusCode(StatusCodes.Status500InternalServerError, "An internal error occurred.");
            }
        }

        [HttpPost("optimize")]
        [ProducesResponseType(typeof(SmartShoppingListResponseModel), StatusCodes.Status200OK)]
        public async Task<IActionResult> OptimizeShoppingList([FromBody] ShoppingListOptimizationModel request)
        {
            try
            {
                var result = await _smartShoppingListService.OptimizeShoppingListAsync(request);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error optimizing shopping list");
                return StatusCode(StatusCodes.Status500InternalServerError, "An internal error occurred.");
            }
        }

        [HttpGet("{id}/suggestions")]
        [ProducesResponseType(typeof(List<ShoppingListSuggestionModel>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetSuggestions(long id)
        {
            try
            {
                var result = await _smartShoppingListService.GetShoppingListSuggestionsAsync(id);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting shopping list suggestions");
                return StatusCode(StatusCodes.Status500InternalServerError, "An internal error occurred.");
            }
        }

        [HttpGet("{id}/analytics")]
        [ProducesResponseType(typeof(ShoppingListAnalyticsModel), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetAnalytics(long id)
        {
            try
            {
                var result = await _smartShoppingListService.GetShoppingListAnalyticsAsync(id);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting shopping list analytics");
                return StatusCode(StatusCodes.Status500InternalServerError, "An internal error occurred.");
            }
        }

        [HttpGet("templates")]
        [ProducesResponseType(typeof(List<ShoppingListTemplateModel>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetTemplates()
        {
            try
            {
                var result = await _smartShoppingListService.GetShoppingListTemplatesAsync();
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting shopping list templates");
                return StatusCode(StatusCodes.Status500InternalServerError, "An internal error occurred.");
            }
        }

        [HttpPost("templates")]
        [ProducesResponseType(typeof(ShoppingListTemplateModel), StatusCodes.Status200OK)]
        public async Task<IActionResult> CreateTemplate([FromBody] ShoppingListTemplateModel request)
        {
            try
            {
                var result = await _smartShoppingListService.CreateShoppingListTemplateAsync(request);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating shopping list template");
                return StatusCode(StatusCodes.Status500InternalServerError, "An internal error occurred.");
            }
        }

        [HttpGet("{id}/history")]
        [ProducesResponseType(typeof(List<ShoppingListGenerationHistoryModel>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetGenerationHistory(long id)
        {
            try
            {
                var result = await _smartShoppingListService.GetGenerationHistoryAsync(id);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting generation history");
                return StatusCode(StatusCodes.Status500InternalServerError, "An internal error occurred.");
            }
        }

        [HttpPost("merge-items")]
        [ProducesResponseType(typeof(List<SmartShoppingListItemModel>), StatusCodes.Status200OK)]
        public async Task<IActionResult> MergeItems([FromBody] List<SmartShoppingListItemModel> items)
        {
            try
            {
                var result = await _smartShoppingListService.MergeShoppingListItemsAsync(items);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error merging shopping list items");
                return StatusCode(StatusCodes.Status500InternalServerError, "An internal error occurred.");
            }
        }

        [HttpPost("substitutions")]
        [ProducesResponseType(typeof(List<ShoppingListSuggestionModel>), StatusCodes.Status200OK)]
        public async Task<IActionResult> SuggestSubstitutions([FromBody] List<SmartShoppingListItemModel> items)
        {
            try
            {
                var result = await _smartShoppingListService.SuggestSubstitutionsAsync(items);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error suggesting substitutions");
                return StatusCode(StatusCodes.Status500InternalServerError, "An internal error occurred.");
            }
        }

        [HttpPost("estimate-cost")]
        [ProducesResponseType(typeof(decimal), StatusCodes.Status200OK)]
        public async Task<IActionResult> EstimateCost([FromBody] List<SmartShoppingListItemModel> items)
        {
            try
            {
                var result = await _smartShoppingListService.EstimateShoppingListCostAsync(items);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error estimating shopping list cost");
                return StatusCode(StatusCodes.Status500InternalServerError, "An internal error occurred.");
            }
        }

        [HttpPost("nutritional-analysis")]
        [ProducesResponseType(typeof(Dictionary<string, object>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetNutritionalAnalysis([FromBody] List<SmartShoppingListItemModel> items)
        {
            try
            {
                var result = await _smartShoppingListService.GetNutritionalAnalysisAsync(items);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting nutritional analysis");
                return StatusCode(StatusCodes.Status500InternalServerError, "An internal error occurred.");
            }
        }
    }
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Webhook;
using System.ComponentModel.DataAnnotations;

namespace Nom.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class WebhookController : BaseApiController
    {
        private readonly IWebhookOrchestrationService _webhookService;
        private readonly ILogger<WebhookController> _logger;

        public WebhookController(
            IWebhookOrchestrationService webhookService,
            ILogger<WebhookController> logger)
        {
            _webhookService = webhookService;
            _logger = logger;
        }

        [HttpGet]
        [ProducesResponseType(typeof(List<WebhookResponseModel>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetWebhooks([FromQuery, Required] long householdId)
        {
            try
            {
                var result = await _webhookService.GetWebhooksAsync(householdId);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting webhooks for household {HouseholdId}", householdId);
                return StatusCode(StatusCodes.Status500InternalServerError, "An internal error occurred.");
            }
        }

        [HttpGet("{id}")]
        [ProducesResponseType(typeof(WebhookResponseModel), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetWebhook(long id)
        {
            try
            {
                var result = await _webhookService.GetWebhookAsync(id);
                if (result == null) return NotFound();
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting webhook {WebhookId}", id);
                return StatusCode(StatusCodes.Status500InternalServerError, "An internal error occurred.");
            }
        }

        [HttpPost]
        [ProducesResponseType(typeof(long), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> CreateWebhook([FromBody] WebhookCreateModel model)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            try
            {
                var id = await _webhookService.CreateWebhookAsync(model);
                return Ok(id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating webhook");
                return StatusCode(StatusCodes.Status500InternalServerError, "An internal error occurred.");
            }
        }

        [HttpPut("{id}")]
        [ProducesResponseType(typeof(WebhookResponseModel), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> UpdateWebhook(long id, [FromBody] WebhookUpdateModel model)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            try
            {
                var result = await _webhookService.UpdateWebhookAsync(id, model);
                if (result == null) return NotFound();
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating webhook {WebhookId}", id);
                return StatusCode(StatusCodes.Status500InternalServerError, "An internal error occurred.");
            }
        }

        [HttpDelete("{id}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> DeleteWebhook(long id)
        {
            try
            {
                var success = await _webhookService.DeleteWebhookAsync(id);
                if (!success) return NotFound();
                return Ok(new { Message = "Webhook deleted successfully." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting webhook {WebhookId}", id);
                return StatusCode(StatusCodes.Status500InternalServerError, "An internal error occurred.");
            }
        }

        [HttpPost("{id}/test")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> TestWebhook(long id)
        {
            try
            {
                var success = await _webhookService.TestWebhookAsync(id);
                return Ok(new { Success = success, Message = success ? "Webhook test succeeded." : "Webhook test failed." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error testing webhook {WebhookId}", id);
                return StatusCode(StatusCodes.Status500InternalServerError, "An internal error occurred.");
            }
        }
    }
}

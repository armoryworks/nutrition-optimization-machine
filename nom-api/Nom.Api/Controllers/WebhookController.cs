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

        public WebhookController(
            IWebhookOrchestrationService webhookService)
        {
            _webhookService = webhookService;
        }

        [HttpGet]
        [ProducesResponseType(typeof(List<WebhookResponseModel>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetWebhooks([FromQuery, Required] long householdId)
        {
            var result = await _webhookService.GetWebhooksAsync(householdId);
            return Ok(result);
        }

        [HttpGet("{id}")]
        [ProducesResponseType(typeof(WebhookResponseModel), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetWebhook(long id)
        {
            var result = await _webhookService.GetWebhookAsync(id);
            if (result == null) return NotFound();
            return Ok(result);
        }

        [HttpPost]
        [ProducesResponseType(typeof(long), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> CreateWebhook([FromBody] WebhookCreateModel model)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var id = await _webhookService.CreateWebhookAsync(model);
            return Ok(id);
        }

        [HttpPut("{id}")]
        [ProducesResponseType(typeof(WebhookResponseModel), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> UpdateWebhook(long id, [FromBody] WebhookUpdateModel model)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var result = await _webhookService.UpdateWebhookAsync(id, model);
            if (result == null) return NotFound();
            return Ok(result);
        }

        [HttpDelete("{id}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> DeleteWebhook(long id)
        {
            var success = await _webhookService.DeleteWebhookAsync(id);
            if (!success) return NotFound();
            return Ok(new { Message = "Webhook deleted successfully." });
        }

        [HttpPost("{id}/test")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> TestWebhook(long id)
        {
            var success = await _webhookService.TestWebhookAsync(id);
            return Ok(new { Success = success, Message = success ? "Webhook test succeeded." : "Webhook test failed." });
        }
    }
}

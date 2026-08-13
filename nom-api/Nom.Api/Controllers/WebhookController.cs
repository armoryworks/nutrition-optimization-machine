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
            if (!IsHouseholdMember(householdId)) return Forbid();

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
            if (!IsHouseholdMember(result.HouseholdId)) return Forbid();
            return Ok(result);
        }

        [HttpPost]
        [ProducesResponseType(typeof(long), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> CreateWebhook([FromBody] WebhookCreateModel model)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            // Webhooks trigger outbound requests — creation is a management action.
            if (!CanManageHousehold(model.HouseholdId)) return Forbid();

            try
            {
                var id = await _webhookService.CreateWebhookAsync(model);
                return CreatedAtAction(nameof(GetWebhook), new { id }, id);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPut("{id}")]
        [ProducesResponseType(typeof(WebhookResponseModel), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> UpdateWebhook(long id, [FromBody] WebhookUpdateModel model)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var existing = await _webhookService.GetWebhookAsync(id);
            if (existing == null) return NotFound();
            if (!CanManageHousehold(existing.HouseholdId)) return Forbid();

            try
            {
                var result = await _webhookService.UpdateWebhookAsync(id, model);
                if (result == null) return NotFound();
                return Ok(result);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpDelete("{id}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> DeleteWebhook(long id)
        {
            var existing = await _webhookService.GetWebhookAsync(id);
            if (existing == null) return NotFound();
            if (!CanManageHousehold(existing.HouseholdId)) return Forbid();

            var success = await _webhookService.DeleteWebhookAsync(id);
            if (!success) return NotFound();
            return Ok(new { Message = "Webhook deleted successfully." });
        }

        [HttpPost("{id}/test")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> TestWebhook(long id)
        {
            var existing = await _webhookService.GetWebhookAsync(id);
            if (existing == null) return NotFound();
            // Triggers a server-side outbound request — require management rights.
            if (!CanManageHousehold(existing.HouseholdId)) return Forbid();

            var success = await _webhookService.TestWebhookAsync(id);
            return Ok(new { Success = success, Message = success ? "Webhook test succeeded." : "Webhook test failed." });
        }
    }
}

// File: Nom.Api/Controllers/PrivacyController.cs

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Privacy;
using System.Threading.Tasks;

namespace Nom.Api.Controllers
{
    /// <summary>
    /// Exposes API endpoints for managing user privacy, consent, and data subject rights
    /// in compliance with GDPR. All endpoints require authentication.
    /// </summary>
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class PrivacyController : BaseApiController
    {
        private readonly IPrivacyOrchestrationService _privacyOrchestrationService;
        private readonly IPersonOrchestrationService _personOrchestrationService;

        public PrivacyController(
            IPrivacyOrchestrationService privacyOrchestrationService,
            IPersonOrchestrationService personOrchestrationService)
        {
            _privacyOrchestrationService = privacyOrchestrationService;
            _personOrchestrationService = personOrchestrationService;
        }

        /// <summary>
        /// Updates the consent settings for the currently authenticated user.
        /// </summary>
        /// <param name="request">A list of consent preferences to update.</param>
        /// <returns>A confirmation of the update.</returns>
        [HttpPost("consent")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> UpdateConsent([FromBody] UpdateConsentRequest request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var personId = _personOrchestrationService.GetCurrentPersonIdRequired();
            var success = await _privacyOrchestrationService.UpdateConsentAsync(request, personId);
            if (success)
            {
                return Ok(new { Message = "Consent settings updated successfully." });
            }

            return StatusCode(StatusCodes.Status500InternalServerError, new { Message = "Failed to update consent settings." });
        }

        /// <summary>
        /// Initiates a request to export the user's personal data.
        /// </summary>
        /// <param name="request">The data export request details.</param>
        /// <returns>A response indicating the request has been queued.</returns>
        [HttpPost("data-export")]
        [ProducesResponseType(typeof(PrivacyRequestStatusResponse), StatusCodes.Status202Accepted)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> RequestDataExport([FromBody] DataExportRequest request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var personId = _personOrchestrationService.GetCurrentPersonIdRequired();
            var response = await _privacyOrchestrationService.RequestDataExportAsync(request, personId);
            return Accepted(response);
        }

        /// <summary>
        /// Initiates a request for account and data deletion.
        /// </summary>
        /// <param name="request">The data deletion confirmation request.</param>
        /// <returns>A response indicating the request has been queued.</returns>
        [HttpPost("data-deletion")]
        [ProducesResponseType(typeof(PrivacyRequestStatusResponse), StatusCodes.Status202Accepted)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> RequestDataDeletion([FromBody] DataDeletionRequest request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var personId = _personOrchestrationService.GetCurrentPersonIdRequired();
            var response = await _privacyOrchestrationService.RequestDataDeletionAsync(request, personId);
            if (!response.Success)
            {
                return BadRequest(response);
            }

            return Accepted(response);
        }
    }
}

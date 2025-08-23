// File: Nom.Api/Controllers/PersonController.cs

using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;
using Nom.Orch.Models.Person;
using Nom.Orch.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace Nom.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class PersonController : BaseApiController
    {
        private readonly IPersonOrchestrationService _personOrchestrationService;
        private readonly ILogger<PersonController> _logger;

        public PersonController(
            IPersonOrchestrationService personOrchestrationService,
            ILogger<PersonController> logger)
        {
            _personOrchestrationService = personOrchestrationService;
            _logger = logger;
        }

        /// <summary>
        /// Creates or updates the person profile linked to the authenticated user.
        /// Prevents the creation of duplicate person records for the same user.
        /// </summary>
        /// <param name="model">The person creation request data, including the person's name.</param>
        /// <returns>The created or updated Person profile data.</returns>
        [HttpPost]
        [ProducesResponseType(typeof(PersonCreateResponseModel), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(PersonCreateResponseModel), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> UpsertPerson([FromBody] PersonCreateModel model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            try
            {
                // The UpsertPersonAsync method now contains the logic to check for existing users.
                var response = await _personOrchestrationService.UpsertPersonAsync(model);

                // Determine if the resource was created or updated for the response code.
                bool wasCreated = !HttpContext.Response.Headers.ContainsKey("Location");

                if (wasCreated)
                {
                    return CreatedAtAction(nameof(GetPersonById), new { id = response.Id }, response);
                }
                else
                {
                    return Ok(response);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred in UpsertPerson.");
                return StatusCode(StatusCodes.Status500InternalServerError, "An internal error occurred.");
            }
        }

        [HttpGet("{id}", Name = "GetPersonById")]
        public IActionResult GetPersonById(long id)
        {
            // Placeholder implementation
            _logger.LogInformation("Attempting to get person with ID: {PersonId}", id);
            return NotFound();
        }

        [HttpGet("onboarding-state")]
        [AllowAnonymous] // Allow fetching onboarding state without authentication
        public async Task<IActionResult> GetOnboardingState([FromQuery] string? userId = null)
        {
            try
            {
                var onboardingState = await _personOrchestrationService.GetOnboardingStateAsync(userId);
                return Ok(onboardingState);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching onboarding state");
                return StatusCode(500, new { message = "Failed to fetch onboarding state" });
            }
        }

        [HttpPost("onboarding-complete")]
        [AllowAnonymous] // Allow onboarding completion without authentication
        public async Task<IActionResult> OnboardingComplete([FromBody] OnboardingCompleteRequest request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var response = await _personOrchestrationService.CompleteOnboardingAsync(request);

            if (response.Success)
            {
                return Ok(response);
            }
            return StatusCode(500, new { message = response.Message });
        }
    }
}

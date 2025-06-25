// Nom.Api/Controllers/PersonController.cs
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;
using Nom.Orch.Models.Person;
using Nom.Orch.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Authorization;

namespace Nom.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class PersonController : ControllerBase
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
        /// Creates a new person profile and links it to an existing Identity user.
        /// This endpoint assumes the Identity user has already been registered via another mechanism.
        /// </summary>
        /// <param name="model">The person creation request data, including the person's name.</param>
        /// <returns>The created Person profile data.</returns>
        [HttpPost] // POST to /api/Person
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> CreatePerson([FromBody] PersonCreateModel model)
        {
            if (!ModelState.IsValid)
            {
                _logger.LogWarning("CreatePerson: Invalid ModelState for request.");
                return BadRequest(ModelState);
            }

            try
            {
                // Infer IdentityUserId from the context user
                var identityUserId = User?.Identity?.Name; // Assuming Name contains the IdentityUserId
                if (string.IsNullOrEmpty(identityUserId))
                {
                    _logger.LogWarning("CreatePerson: Unable to infer IdentityUserId from the context user.");
                    return Unauthorized(new { Message = "User identity could not be determined." });
                }

                var personEntity = await _personOrchestrationService.SetupNewRegisteredPersonAsync(identityUserId, model.PersonName);

                if (personEntity == null || personEntity.Id <= 0)
                {
                    _logger.LogError("CreatePerson: Failed to create person entity for Identity user {IdentityUserId}", identityUserId);
                    return StatusCode(StatusCodes.Status500InternalServerError, new { Message = "Failed to create person entity." });
                }

                _logger.LogInformation("CreatePerson: Person entity {PersonId} created and linked to Identity user {IdentityUserId}", personEntity.Id, identityUserId);

                var responseModel = new PersonCreateResponseModel
                {
                    Id = personEntity.Id,
                    Name = personEntity.Name,
                    UserId = personEntity.UserId
                };

                return CreatedAtRoute("GetPersonById", new { id = responseModel.Id }, responseModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CreatePerson: Failed to create person profile for Identity user.");
                return StatusCode(StatusCodes.Status500InternalServerError, new { Message = "An error occurred during person profile creation." });
            }
        }

        /// <summary>
        /// Placeholder for retrieving a person profile by ID.
        /// </summary>
        /// <param name="id">The ID of the person to retrieve.</param>
        /// <returns>The person profile data.</returns>
        [HttpGet("{id}", Name = "GetPersonById")] // GET /api/Person/{id}
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public IActionResult GetPersonById(long id)
        {
            // This is a placeholder. In a real scenario, you'd fetch the person from your service layer.
            _logger.LogInformation("Attempting to get person with ID: {PersonId}", id);
            return NotFound(); // Placeholder: No actual implementation to fetch yet
        }

        /// <summary>
        /// Handles the complete onboarding process for a newly registered or existing user.
        /// Receives consolidated person details, attributes, and restrictions.
        /// </summary>
        /// <param name="request">The comprehensive onboarding data.</param>
        [HttpPost("onboarding-complete")]
        public async Task<IActionResult> OnboardingComplete([FromBody] OnboardingCompleteRequest request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var success = await _personOrchestrationService.CompleteOnboardingAsync(request);

            if (success.Success)
            {
                return Ok(new { message = "Onboarding completed successfully." });
            }
            return StatusCode(500, new { message = "Failed to complete onboarding due to an internal error." });
        }
    }
}
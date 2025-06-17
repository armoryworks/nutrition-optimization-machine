// Nom.Api/Controllers/PersonController.cs
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;
using Nom.Api.Models.Person;
using Nom.Orch.Interfaces;
using Microsoft.Extensions.Logging;

namespace Nom.Api.Controllers
{
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
        /// <param name="model">The person creation request data, including the Identity User ID and person's name.</param>
        /// <returns>The created Person profile data.</returns>
        [HttpPost] // POST to /api/Person
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> CreatePerson([FromBody] PersonCreateModel model)
        {
            if (!ModelState.IsValid)
            {
                _logger.LogWarning("CreatePerson: Invalid ModelState for request with IdentityUserId: {IdentityUserId}", model.IdentityUserId);
                return BadRequest(ModelState);
            }

            try
            {
                // Delegate to the orchestration service to create the Person entity
                var personEntity = await _personOrchestrationService.SetupNewRegisteredPersonAsync(model.IdentityUserId, model.PersonName);

                _logger.LogInformation("CreatePerson: Person entity {PersonId} created and linked to Identity user {IdentityUserId}", personEntity.Id, personEntity.UserId);

                // Map the PersonEntity to the PersonCreateResponseModel for the API response
                var responseModel = new PersonCreateResponseModel
                {
                    Id = personEntity.Id,
                    Name = personEntity.Name,
                    UserId = personEntity.UserId
                    // REMOVED: InvitationCode = personEntity.InvitationCode
                    // InvitationCode is no longer included in the response sent to the frontend.
                };

                return CreatedAtAction(nameof(GetPersonById), new { id = responseModel.Id }, responseModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CreatePerson: Failed to create person profile for Identity user {IdentityUserId}", model.IdentityUserId);
                ModelState.AddModelError(string.Empty, "An error occurred during person profile creation.");
                return StatusCode(StatusCodes.Status500InternalServerError, ModelState);
            }
        }

        /// <summary>
        /// Placeholder for retrieving a person profile by ID.
        /// </summary>
        /// <param name="id">The ID of the person to retrieve.</param>
        /// <returns>The person profile data.</returns>
        [HttpGet("{id}")] // GET /api/Person/{id}
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public IActionResult GetPersonById(long id)
        {
            // This is a placeholder. In a real scenario, you'd fetch the person from your service layer.
            _logger.LogInformation("Attempting to get person with ID: {PersonId}", id);
            return NotFound(); // Placeholder: No actual implementation to fetch yet
        }
    }
}
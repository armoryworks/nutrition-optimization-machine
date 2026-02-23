// File: Nom.Api/Controllers/PersonController.cs

using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
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
        /// Returns true if the target person is the current user or shares a household.
        /// </summary>
        private async Task<bool> CanAccessPersonAsync(long targetPersonId)
        {
            var currentPersonId = GetCurrentPersonId();
            if (currentPersonId.HasValue && currentPersonId.Value == targetPersonId)
                return true;

            return await _personOrchestrationService.IsPersonInHouseholdsAsync(targetPersonId, GetUserHouseholdIds());
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

        /// <summary>
        /// Gets the Person entity for the currently authenticated user.
        /// </summary>
        [HttpGet("me")]
        [ProducesResponseType(typeof(PersonModel), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetCurrentPerson()
        {
            try
            {
                var personId = _personOrchestrationService.GetCurrentPersonId();
                if (!personId.HasValue)
                {
                    return NotFound(new { message = "No person profile found for the current user." });
                }

                var person = await _personOrchestrationService.GetPersonByIdAsync(personId.Value);
                return Ok(person);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving current person");
                return StatusCode(StatusCodes.Status500InternalServerError, "An internal error occurred.");
            }
        }

        /// <summary>
        /// Gets persons visible to the current user (household co-members).
        /// </summary>
        [HttpGet]
        [ProducesResponseType(typeof(List<PersonModel>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> GetAllPersons()
        {
            try
            {
                var householdIds = GetUserHouseholdIds();
                var persons = await _personOrchestrationService.GetPersonsForHouseholdsAsync(householdIds);
                return Ok(persons);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving persons");
                return StatusCode(StatusCodes.Status500InternalServerError, "An internal error occurred.");
            }
        }

        /// <summary>
        /// Gets a specific person by ID (must be self or share a household)
        /// </summary>
        [HttpGet("{id:long}", Name = "GetPersonById")]
        [ProducesResponseType(typeof(PersonModel), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetPersonById(long id)
        {
            try
            {
                if (!await CanAccessPersonAsync(id))
                    return Forbid();

                var person = await _personOrchestrationService.GetPersonByIdAsync(id);
                if (person == null)
                {
                    return NotFound();
                }
                return Ok(person);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving person {PersonId}", id);
                return StatusCode(StatusCodes.Status500InternalServerError, "An internal error occurred.");
            }
        }

        /// <summary>
        /// Updates a person's information (must be self or share a household)
        /// </summary>
        [HttpPut("{id:long}")]
        [ProducesResponseType(typeof(PersonModel), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> UpdatePerson(long id, [FromBody] UpdatePersonRequest request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            if (id != request.Id)
            {
                return BadRequest("ID mismatch");
            }

            if (!await CanAccessPersonAsync(id))
                return Forbid();

            try
            {
                var updatedPerson = await _personOrchestrationService.UpdatePersonAsync(request);
                return Ok(updatedPerson);
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating person {PersonId}", id);
                return StatusCode(StatusCodes.Status500InternalServerError, "An internal error occurred.");
            }
        }

        /// <summary>
        /// Deletes a person (must be self or share a household)
        /// </summary>
        [HttpDelete("{id:long}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> DeletePerson(long id)
        {
            if (!await CanAccessPersonAsync(id))
                return Forbid();

            try
            {
                var result = await _personOrchestrationService.DeletePersonAsync(id);
                if (!result)
                {
                    return NotFound();
                }
                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting person {PersonId}", id);
                return StatusCode(StatusCodes.Status500InternalServerError, "An internal error occurred.");
            }
        }

        [HttpGet("search")]
        [ProducesResponseType(typeof(List<PersonModel>), StatusCodes.Status200OK)]
        public async Task<IActionResult> SearchPersons([FromQuery] string query, [FromQuery] int limit = 10)
        {
            try
            {
                var results = await _personOrchestrationService.SearchPersonsAsync(query, Math.Min(limit, 10));
                return Ok(results);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching persons");
                return StatusCode(StatusCodes.Status500InternalServerError, "An internal error occurred.");
            }
        }

        /// <summary>
        /// Saves a person's profile (name + attributes).
        /// Replaces all existing attributes. Pass id=0 to create a new person.
        /// </summary>
        [HttpPut("{id:long}/profile")]
        [ProducesResponseType(typeof(PersonModel), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> SaveProfile(long id, [FromBody] SaveProfileRequest request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            // id=0 means creating a new dependent — allow if user has a household
            if (id != 0 && !await CanAccessPersonAsync(id))
                return Forbid();

            try
            {
                var person = await _personOrchestrationService.SaveProfileAsync(id, request);
                return Ok(person);
            }
            catch (UnauthorizedAccessException)
            {
                return Unauthorized();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving profile for person {PersonId}", id);
                return StatusCode(StatusCodes.Status500InternalServerError, "An internal error occurred.");
            }
        }

        /// <summary>
        /// Saves person-level restrictions (before a plan exists).
        /// Replaces all existing person-level restrictions.
        /// </summary>
        [HttpPut("{id:long}/restrictions")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> SaveRestrictions(long id, [FromBody] List<RestrictionRequest> restrictions)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            if (!await CanAccessPersonAsync(id))
                return Forbid();

            try
            {
                await _personOrchestrationService.SaveRestrictionsAsync(id, restrictions);
                return NoContent();
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving restrictions for person {PersonId}", id);
                return StatusCode(StatusCodes.Status500InternalServerError, "An internal error occurred.");
            }
        }

        /// <summary>
        /// Gets the onboarding state for a specific person (self only).
        /// </summary>
        [HttpGet("{id:long}/onboarding")]
        [ProducesResponseType(typeof(OnboardingStateResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetOnboardingState(long id)
        {
            var currentPersonId = GetCurrentPersonId();
            if (!currentPersonId.HasValue || currentPersonId.Value != id)
                return Forbid();

            try
            {
                var onboardingState = await _personOrchestrationService.GetOnboardingStateAsync(id);
                return Ok(onboardingState);
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching onboarding state for person {PersonId}", id);
                return StatusCode(500, new { message = "Failed to fetch onboarding state" });
            }
        }

        /// <summary>
        /// Completes onboarding for a specific person (self only).
        /// </summary>
        [HttpPost("{id:long}/onboarding")]
        [ProducesResponseType(typeof(OnboardingCompleteResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> CompleteOnboarding(long id, [FromBody] OnboardingCompleteRequest request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var currentPersonId = GetCurrentPersonId();
            if (!currentPersonId.HasValue || currentPersonId.Value != id)
                return Forbid();

            // Ensure the request uses the person ID from the URL
            request.PersonId = id;

            var response = await _personOrchestrationService.CompleteOnboardingAsync(request);

            if (response.Success)
            {
                return Ok(response);
            }
            return StatusCode(500, new { message = response.Message });
        }
    }
}

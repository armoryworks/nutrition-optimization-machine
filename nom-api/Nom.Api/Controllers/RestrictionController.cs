using Microsoft.AspNetCore.Mvc;
using Nom.Orch.Interfaces;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;

namespace Nom.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize] // Most restriction-related actions should be authorized
    public class RestrictionController : ControllerBase
    {
        private readonly IRestrictionOrchestrationService _restrictionOrchestrationService;

        public RestrictionController(IRestrictionOrchestrationService restrictionOrchestrationService)
        {
            _restrictionOrchestrationService = restrictionOrchestrationService;
        }

        /// <summary>
        /// Retrieves a list of curated ingredients for use in restriction definitions.
        /// </summary>
        [HttpGet("curated-ingredients")]
        public async Task<IActionResult> GetCuratedIngredients()
        {
            var ingredients = await _restrictionOrchestrationService.GetCuratedIngredientsAsync();
            return Ok(ingredients);
        }

        /// <summary>
        /// Retrieves a list of micronutrients for use in restriction definitions.
        /// </summary>
        [HttpGet("micronutrients")]
        public async Task<IActionResult> GetMicronutrients()
        {
            var micronutrients = await _restrictionOrchestrationService.GetMicronutrientsAsync();
            return Ok(micronutrients);
        }

        // Potentially other endpoints for managing restriction types, etc.
    }
}

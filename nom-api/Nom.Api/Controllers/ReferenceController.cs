// File: Nom.Api/Controllers/ReferenceController.cs

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nom.Orch.Interfaces;
using System.Threading.Tasks;

namespace Nom.Api.Controllers
{
    [Authorize]
    public class ReferenceController : BaseApiController
    {
        private readonly IReferenceOrchestrationService _referenceOrch;

        public ReferenceController(IReferenceOrchestrationService referenceOrch)
        {
            _referenceOrch = referenceOrch;
        }

        /// <summary>
        /// Gets all references for a specific reference group.
        /// </summary>
        /// <param name="discriminatorId">The discriminator ID for the reference group</param>
        /// <returns>List of references for the specified group</returns>
        [HttpGet("{discriminatorId}/all")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(List<Nom.Data.Reference.GroupedReferenceViewEntity>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetReferencesByGroup(long discriminatorId)
        {
            var references = await _referenceOrch.GetReferencesByGroupAsync(discriminatorId);
            return Ok(references);
        }

        /// <summary>
        /// Gets multiple reference groups in one call for performance.
        /// </summary>
        /// <param name="discriminatorIds">Array of discriminator IDs</param>
        /// <returns>Dictionary of discriminator ID to references mapping</returns>
        [HttpPost("bulk")]
        [ProducesResponseType(typeof(Dictionary<long, List<Nom.Data.Reference.GroupedReferenceViewEntity>>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetReferencesBulk([FromBody] long[] discriminatorIds)
        {
            if (discriminatorIds == null || discriminatorIds.Length == 0)
            {
                return BadRequest(new { message = "Discriminator IDs array cannot be null or empty." });
            }

            var references = await _referenceOrch.GetReferencesBulkAsync(discriminatorIds);
            return Ok(references);
        }
    }
}
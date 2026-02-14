using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Label;

namespace Nom.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class LabelController : BaseApiController
    {
        private readonly ILabelOrchestrationService _labelService;
        private readonly ILogger<LabelController> _logger;

        public LabelController(
            ILabelOrchestrationService labelService,
            ILogger<LabelController> logger)
        {
            _labelService = labelService;
            _logger = logger;
        }

        [HttpGet]
        [ProducesResponseType(typeof(List<LabelResponseModel>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetLabels()
        {
            try
            {
                var result = await _labelService.GetLabelsAsync();
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting labels");
                return StatusCode(StatusCodes.Status500InternalServerError, "An internal error occurred.");
            }
        }

        [HttpPost]
        [ProducesResponseType(typeof(long), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> CreateLabel([FromBody] LabelCreateModel model)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            try
            {
                var id = await _labelService.CreateLabelAsync(model);
                return Ok(id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating label");
                return StatusCode(StatusCodes.Status500InternalServerError, "An internal error occurred.");
            }
        }

        [HttpPut("{id}")]
        [ProducesResponseType(typeof(LabelResponseModel), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> UpdateLabel(long id, [FromBody] LabelCreateModel model)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            try
            {
                var result = await _labelService.UpdateLabelAsync(id, model);
                if (result == null) return NotFound();
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating label {LabelId}", id);
                return StatusCode(StatusCodes.Status500InternalServerError, "An internal error occurred.");
            }
        }

        [HttpDelete("{id}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> DeleteLabel(long id)
        {
            try
            {
                var success = await _labelService.DeleteLabelAsync(id);
                if (!success) return NotFound();
                return Ok(new { Message = "Label deleted successfully." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting label {LabelId}", id);
                return StatusCode(StatusCodes.Status500InternalServerError, "An internal error occurred.");
            }
        }
    }
}

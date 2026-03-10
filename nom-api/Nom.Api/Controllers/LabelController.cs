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

        public LabelController(
            ILabelOrchestrationService labelService)
        {
            _labelService = labelService;
        }

        [HttpGet]
        [ProducesResponseType(typeof(List<LabelResponseModel>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetLabels()
        {
            var result = await _labelService.GetLabelsAsync();
            return Ok(result);
        }

        [HttpPost]
        [ProducesResponseType(typeof(long), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> CreateLabel([FromBody] LabelCreateModel model)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var id = await _labelService.CreateLabelAsync(model);
            return Created($"api/label/{id}", id);
        }

        [HttpPut("{id}")]
        [ProducesResponseType(typeof(LabelResponseModel), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> UpdateLabel(long id, [FromBody] LabelCreateModel model)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var result = await _labelService.UpdateLabelAsync(id, model);
            if (result == null) return NotFound();
            return Ok(result);
        }

        [HttpDelete("{id}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> DeleteLabel(long id)
        {
            var success = await _labelService.DeleteLabelAsync(id);
            if (!success) return NotFound();
            return Ok(new { Message = "Label deleted successfully." });
        }
    }
}

using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Curation;

namespace Nom.Api.Controllers
{
    /// <summary>
    /// Alcohol retention (alcohol design A7), beside the food catalog's other repair routes: read how
    /// much of each alcohol line's alcohol stays after the recipe's method, store it on the line and
    /// recalculate the recipe, or take a run back by its time. Every write is a dry run unless
    /// apply=true. Curation-gated.
    /// </summary>
    [Authorize(Policy = "CanManageCuration")]
    [ApiController]
    [Route("api/FoodCatalog/alcohol-retention")]
    public class AlcoholRetentionController : BaseApiController
    {
        private readonly IAlcoholRetentionService _retention;

        public AlcoholRetentionController(IAlcoholRetentionService retention) => _retention = retention;

        /// <summary>Read every alcohol line of up to maxRecipes recipes after afterRecipeId and store the retention where it changes; a dry run unless apply=true.</summary>
        [HttpPost]
        public async Task<ActionResult<AlcoholRetentionRunResult>> Run([FromQuery] bool apply = false, [FromQuery] int maxRecipes = 20000, [FromQuery] long afterRecipeId = 0, [FromQuery] int sampleSize = 50) =>
            Ok(await _retention.RunAsync(apply, maxRecipes, afterRecipeId, sampleSize, HttpContext.RequestAborted));

        /// <summary>Give back the retention each line had before a run at or after the given UTC time and recalculate its recipe; a dry run unless apply=true.</summary>
        [HttpPost("undo")]
        public async Task<ActionResult<AlcoholRetentionUndoResult>> Undo([FromQuery, Required] DateTime sinceUtc, [FromQuery] bool apply = false) =>
            Ok(await _retention.UndoAsync(DateTime.SpecifyKind(sinceUtc, DateTimeKind.Utc), apply, HttpContext.RequestAborted));
    }
}

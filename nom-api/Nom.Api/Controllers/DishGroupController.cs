using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Recipe;

namespace Nom.Api.Controllers
{
    /// <summary>
    /// Canonical dish groups: browse groups and their visible member recipes;
    /// curation admins can reassign recipes and merge duplicate groups.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class DishGroupController : BaseApiController
    {
        public record AssignRequest(long? DishGroupId, string? DishGroupName);

        private readonly IDishGroupService _dishGroups;
        private readonly ICurrentUserService _currentUser;

        public DishGroupController(IDishGroupService dishGroups, ICurrentUserService currentUser)
        {
            _dishGroups = dishGroups;
            _currentUser = currentUser;
        }

        /// <summary>All dish groups with visible-member counts, largest first.</summary>
        [HttpGet]
        [AllowAnonymous]
        public async Task<ActionResult<List<DishGroupModel>>> List([FromQuery] int limit = 200)
        {
            return Ok(await _dishGroups.ListAsync(limit));
        }

        /// <summary>One group + its recipes visible to the caller.</summary>
        [HttpGet("{slug}")]
        [AllowAnonymous]
        public async Task<ActionResult<DishGroupDetailModel>> Get(string slug)
        {
            var result = await _dishGroups.GetBySlugAsync(slug, _currentUser.PersonId);
            return result == null ? NotFound() : Ok(result);
        }

        /// <summary>
        /// Reassign a recipe's dish group — by id, by name (creating the group
        /// when new), or clear with both fields null.
        /// </summary>
        [HttpPut("recipe/{recipeId:long}")]
        [Authorize(Policy = "CanManageCuration")]
        public async Task<IActionResult> Assign(long recipeId, [FromBody] AssignRequest request)
        {
            var groupId = request.DishGroupId;
            if (groupId == null && !string.IsNullOrWhiteSpace(request.DishGroupName))
            {
                groupId = (await _dishGroups.GetOrCreateAsync(request.DishGroupName)).Id;
            }

            return await _dishGroups.AssignAsync(recipeId, groupId) ? NoContent() : NotFound();
        }

        /// <summary>Merge one group's recipes into another and retire the source.</summary>
        [HttpPost("{sourceId:long}/merge-into/{targetId:long}")]
        [Authorize(Policy = "CanManageCuration")]
        public async Task<IActionResult> Merge(long sourceId, long targetId)
        {
            return await _dishGroups.MergeAsync(sourceId, targetId, _currentUser.RequiredPersonId)
                ? NoContent()
                : NotFound();
        }
    }
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Platform;

namespace Nom.Api.Controllers
{
    /// <summary>
    /// Platform feature switches. Reading the list is admin-only; a single
    /// feature's state is readable by any signed-in user so the apps can hide
    /// entry points for something that is switched off.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class PlatformFeatureController : BaseApiController
    {
        private readonly IPlatformFeatureService _features;
        private readonly ICurrentUserService _currentUser;

        public PlatformFeatureController(IPlatformFeatureService features, ICurrentUserService currentUser)
        {
            _features = features;
            _currentUser = currentUser;
        }

        [HttpGet]
        [Authorize(Policy = "CanManageUserRoles")]
        public async Task<ActionResult<List<PlatformFeatureModel>>> List()
        {
            return Ok(await _features.ListAsync());
        }

        /// <summary>Whether one feature is on — used by the apps to hide dark features.</summary>
        [HttpGet("{key}")]
        [AllowAnonymous]
        public async Task<ActionResult<PlatformFeatureModel>> Get(string key)
        {
            return Ok(new PlatformFeatureModel
            {
                Key = key,
                IsEnabled = await _features.IsEnabledAsync(key),
            });
        }

        [HttpPut("{key}")]
        [Authorize(Policy = "CanManageUserRoles")]
        public async Task<ActionResult<PlatformFeatureModel>> Set(string key, [FromBody] SetPlatformFeatureRequestModel request)
        {
            return Ok(await _features.SetAsync(key, request.IsEnabled, _currentUser.RequiredPersonId));
        }
    }
}

// File: Nom.Api/Controllers/UserManagementController.cs

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.UserManagement;
using System.Threading.Tasks;

namespace Nom.Api.Controllers
{
    [Authorize(Policy = "CanManageUserRoles")] // Requires the role management claim.
    public class UserManagementController : BaseApiController
    {
        private readonly IUserManagementOrchestrationService _userManagementOrch;

        public UserManagementController(IUserManagementOrchestrationService userManagementOrch)
        {
            _userManagementOrch = userManagementOrch;
        }

        [HttpPut("claims")]
        public async Task<IActionResult> UpdateUserClaims([FromBody] UpdateUserClaimsRequest request)
        {
            await _userManagementOrch.UpdateUserClaimsAsync(request);
            return NoContent(); // Success, no content to return.
        }
    }
}
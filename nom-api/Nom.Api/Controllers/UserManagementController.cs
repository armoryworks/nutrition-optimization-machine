// File: Nom.Api/Controllers/UserManagementController.cs

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.UserManagement;
using System;
using System.Threading.Tasks;

namespace Nom.Api.Controllers
{
    [Authorize(Policy = "CanManageUserRoles")] // Requires the role management claim.
    public class UserManagementController : BaseApiController
    {
        private readonly ILogger<UserManagementController> _logger;
        private readonly IUserManagementOrchestrationService _userManagementOrch;

        public UserManagementController(ILogger<UserManagementController> logger, IUserManagementOrchestrationService userManagementOrch)
        {
            _logger = logger;
            _userManagementOrch = userManagementOrch;
        }

        [HttpPut("claims")]
        public async Task<IActionResult> UpdateUserClaims([FromBody] UpdateUserClaimsRequest request)
        {
            try
            {
                await _userManagementOrch.UpdateUserClaimsAsync(request);
                return NoContent(); // Success, no content to return.
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating claims for user {UserId} by admin {AdminPersonId}", request.UserId, GetCurrentPersonId());
                return StatusCode(500, "An unexpected error occurred while updating user claims.");
            }
        }
    }
}
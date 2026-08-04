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

        [HttpGet("users")]
        public async Task<IActionResult> GetAllUsers()
        {
            return Ok(await _userManagementOrch.GetAllUsersAsync());
        }

        [HttpGet("users/{userId}")]
        public async Task<IActionResult> GetUser(string userId)
        {
            return Ok(await _userManagementOrch.GetUserByIdAsync(userId));
        }

        [HttpGet("users/{userId}/claims")]
        public async Task<IActionResult> GetUserClaims(string userId)
        {
            return Ok(await _userManagementOrch.GetUserClaimsAsync(userId));
        }

        [HttpPost("users")]
        public async Task<IActionResult> CreateUser([FromBody] CreateUserRequestModel request)
        {
            var user = await _userManagementOrch.CreateUserAsync(request);
            return CreatedAtAction(nameof(GetUser), new { userId = user.Id }, user);
        }

        [HttpPut("users/{userId}")]
        public async Task<IActionResult> UpdateUser(string userId, [FromBody] UpdateUserRequestModel request)
        {
            return Ok(await _userManagementOrch.UpdateUserAsync(userId, request));
        }

        [HttpDelete("users/{userId}")]
        public async Task<IActionResult> DeleteUser(string userId)
        {
            await _userManagementOrch.DeleteUserAsync(userId);
            return NoContent();
        }
    }
}
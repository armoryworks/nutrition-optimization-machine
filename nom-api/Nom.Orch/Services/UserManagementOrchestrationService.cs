using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.UserManagement;
using System;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Nom.Orch.Services
{
    public class UserManagementOrchestrationService : IUserManagementOrchestrationService
    {
        private readonly UserManager<IdentityUser> _userManager;
        private readonly ILogger<UserManagementOrchestrationService> _logger;

        public UserManagementOrchestrationService(UserManager<IdentityUser> userManager, ILogger<UserManagementOrchestrationService> logger)
        {
            _userManager = userManager;
            _logger = logger;
        }

        public async Task UpdateUserClaimsAsync(UpdateUserClaimsRequest request)
        {
            var user = await _userManager.FindByIdAsync(request.UserId);
            if (user == null)
            {
                throw new Exception($"User with ID {request.UserId} not found.");
            }

            // Logic to add/remove "CanManageCuration" claim
            await UpdateClaimAsync(user, "CanManageCuration", request.CanManageCuration);

            // Logic to add/remove "CanManageUserRoles" claim
            await UpdateClaimAsync(user, "CanManageUserRoles", request.CanManageUserRoles);

            _logger.LogInformation("Updated claims for user {UserId}", request.UserId);
        }

        private async Task UpdateClaimAsync(IdentityUser user, string claimType, bool hasClaim)
        {
            var claim = new Claim(claimType, "true");
            var userClaims = await _userManager.GetClaimsAsync(user);
            var existingClaim = userClaims.FirstOrDefault(c => c.Type == claimType);

            if (hasClaim && existingClaim == null)
            {
                await _userManager.AddClaimAsync(user, claim);
            }
            else if (!hasClaim && existingClaim != null)
            {
                await _userManager.RemoveClaimAsync(user, existingClaim);
            }
        }
    }
}
// File: Nom.Api/Controllers/ClientAdminController.cs

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nom.Orch.Interfaces;
using System.Threading.Tasks;

namespace Nom.Api.Controllers
{
    /// <summary>
    /// Admin portal reads over households ("clients") — the household-centric
    /// counterpart to <see cref="UserManagementController"/>. Same claim gates
    /// both: managing accounts and seeing the client roster are one duty.
    /// </summary>
    [Authorize(Policy = "CanManageUserRoles")]
    public class ClientAdminController : BaseApiController
    {
        private readonly IClientAdminOrchestrationService _clientAdminOrch;

        public ClientAdminController(IClientAdminOrchestrationService clientAdminOrch)
        {
            _clientAdminOrch = clientAdminOrch;
        }

        [HttpGet("households")]
        public async Task<IActionResult> GetHouseholds()
        {
            return Ok(await _clientAdminOrch.GetHouseholdsAsync());
        }

        [HttpGet("households/{householdId:long}/members")]
        public async Task<IActionResult> GetHouseholdMembers(long householdId)
        {
            return Ok(await _clientAdminOrch.GetHouseholdMembersAsync(householdId));
        }
    }
}

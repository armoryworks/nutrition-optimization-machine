// File: Nom.Orch/Services/ClientAdminOrchestrationService.cs

using Microsoft.EntityFrameworkCore;
using Nom.Data;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.UserManagement;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Nom.Orch.Services
{
    public class ClientAdminOrchestrationService : IClientAdminOrchestrationService
    {
        private readonly ApplicationDbContext _context;

        public ClientAdminOrchestrationService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<List<AdminHouseholdModel>> GetHouseholdsAsync()
        {
            return await _context.Households
                .AsNoTracking()
                .OrderBy(h => h.Name)
                .Select(h => new AdminHouseholdModel
                {
                    Id = h.Id,
                    Name = h.Name,
                    IsPersonal = h.IsPersonal,
                    ManagedBy = h.ManagedBy,
                    MemberCount = _context.HouseholdMembers.Count(m => m.HouseholdId == h.Id),
                    ActiveMemberCount = _context.HouseholdMembers.Count(m => m.HouseholdId == h.Id && m.IsActive),
                    CreatedDate = h.CreatedDate,
                    LastPlanDate = _context.MealPlans
                        .Where(p => p.HouseholdId == h.Id)
                        .Select(p => (DateOnly?)p.Date)
                        .Max(),
                })
                .ToListAsync();
        }

        public async Task<List<AdminHouseholdMemberModel>> GetHouseholdMembersAsync(long householdId)
        {
            return await _context.HouseholdMembers
                .AsNoTracking()
                .Where(m => m.HouseholdId == householdId)
                .OrderBy(m => m.Person.Name)
                .Select(m => new AdminHouseholdMemberModel
                {
                    PersonId = m.PersonId,
                    Name = m.Person.Name,
                    UserId = m.Person.UserId,
                    Email = m.Person.Email,
                    Role = m.Role,
                    IsActive = m.IsActive,
                    IsAdmin = m.IsAdmin,
                    JoinedDate = m.JoinedDate,
                })
                .ToListAsync();
        }
    }
}

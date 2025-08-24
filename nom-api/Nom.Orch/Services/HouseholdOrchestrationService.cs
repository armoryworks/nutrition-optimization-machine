// File: Nom.Orch/Services/HouseholdOrchestrationService.cs

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nom.Data;
using Nom.Data.Person;
using Nom.Data.Plan;
using Nom.Data.Reference;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Household;

namespace Nom.Orch.Services
{
    public class HouseholdOrchestrationService : IHouseholdOrchestrationService
    {
        private readonly ApplicationDbContext _context;

        public HouseholdOrchestrationService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<List<HouseholdResponseModel>> GetAllHouseholdsAsync()
        {
            var households = await _context.Households
                .Include(h => h.Members)
                .ToListAsync();

            return households.Select(h => new HouseholdResponseModel
            {
                Id = h.Id,
                Name = h.Name,
                Description = h.Description,
                GroupId = h.GroupId,
                CreatedDate = h.CreatedDate,
                ModifiedDate = h.LastModifiedDate
            }).ToList();
        }

        public async Task<HouseholdCreateResponseModel> CreateHouseholdAsync(HouseholdCreateModel model)
        {
            var household = new HouseholdEntity
            {
                Name = model.Name,
                Description = model.Description,
                GroupId = model.GroupId,
                CreatedDate = DateTime.UtcNow,
                LastModifiedDate = DateTime.UtcNow
            };

            _context.Households.Add(household);
            await _context.SaveChangesAsync();

            return new HouseholdCreateResponseModel
            {
                Id = household.Id,
                Name = household.Name,
                Description = household.Description,
                GroupId = household.GroupId,
                CreatedDate = household.CreatedDate
            };
        }

        public async Task<HouseholdResponseModel?> GetHouseholdAsync(long id)
        {
            var household = await _context.Households
                .Include(h => h.Members)
                .FirstOrDefaultAsync(h => h.Id == id);

            if (household == null)
                return null;

            return new HouseholdResponseModel
            {
                Id = household.Id,
                Name = household.Name,
                Description = household.Description,
                GroupId = household.GroupId,
                CreatedDate = household.CreatedDate,
                ModifiedDate = household.LastModifiedDate
            };
        }

        public async Task<HouseholdResponseModel?> UpdateHouseholdAsync(long id, HouseholdUpdateModel model)
        {
            var household = await _context.Households.FindAsync(id);
            if (household == null)
                return null;

            household.Name = model.Name;
            household.Description = model.Description;
            household.GroupId = model.GroupId ?? household.GroupId; // Keep existing value if null
            household.LastModifiedDate = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return new HouseholdResponseModel
            {
                Id = household.Id,
                Name = household.Name,
                Description = household.Description,
                GroupId = household.GroupId,
                CreatedDate = household.CreatedDate,
                ModifiedDate = household.LastModifiedDate
            };
        }

        public async Task<bool> DeleteHouseholdAsync(long id)
        {
            var household = await _context.Households.FindAsync(id);
            if (household == null)
                return false;

            _context.Households.Remove(household);
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<HouseholdInviteTokenResponseModel> CreateInviteTokenAsync(HouseholdInviteTokenCreateModel model)
        {
            var token = new HouseholdInviteTokenEntity
            {
                HouseholdId = model.HouseholdId,
                Token = Guid.NewGuid().ToString(),
                CreatedDate = DateTime.UtcNow,
                LastModifiedDate = DateTime.UtcNow
            };

            _context.HouseholdInviteTokens.Add(token);
            await _context.SaveChangesAsync();

            return new HouseholdInviteTokenResponseModel
            {
                Id = token.Id,
                HouseholdId = token.HouseholdId,
                Token = token.Token,
                CreatedDate = token.CreatedDate
            };
        }

        public async Task<HouseholdMemberResponseModel> AddMemberAsync(HouseholdMemberCreateModel model)
        {
            try
            {
                // Verify the household exists
                var household = await _context.Households
                    .FirstOrDefaultAsync(h => h.Id == model.HouseholdId);
                
                if (household == null)
                {
                    throw new InvalidOperationException($"Household with ID {model.HouseholdId} not found");
                }

                // Verify the person exists
                var person = await _context.Persons
                    .FirstOrDefaultAsync(p => p.Id == model.PersonId);
                
                if (person == null)
                {
                    throw new InvalidOperationException($"Person with ID {model.PersonId} not found");
                }

                // Check if member already exists
                var existingMember = await _context.HouseholdMembers
                    .FirstOrDefaultAsync(hm => hm.HouseholdId == model.HouseholdId && hm.PersonId == model.PersonId);
                
                if (existingMember != null)
                {
                    throw new InvalidOperationException($"Person {person.Name} is already a member of this household");
                }

                // Create the household member
                var householdMember = new HouseholdMemberEntity
                {
                    HouseholdId = model.HouseholdId,
                    PersonId = model.PersonId,
                    Role = model.Role ?? "Member",
                    CreatedDate = DateTime.UtcNow,
                    CreatedByPersonId = model.PersonId // Self-created
                };

                _context.HouseholdMembers.Add(householdMember);
                await _context.SaveChangesAsync();

                return new HouseholdMemberResponseModel
                {
                    Id = householdMember.Id,
                    HouseholdId = householdMember.HouseholdId,
                    PersonId = householdMember.PersonId,
                    PersonName = person.Name,
                    Role = householdMember.Role,
                    JoinedDate = householdMember.CreatedDate
                };
            }
            catch (Exception ex)
            {
                // Log the error and rethrow
                throw new InvalidOperationException($"Failed to add member to household: {ex.Message}", ex);
            }
        }
    }
} 
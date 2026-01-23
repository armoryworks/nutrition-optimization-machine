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
using Nom.Data.Recipe;
using Nom.Data.Reference;
using Nom.Data.Shopping;
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
                .FirstOrDefaultAsync(h => h.Id == id);

            if (household == null)
                return null;

            // Get household members with person details and email from Identity User table
            var members = await (from hm in _context.HouseholdMembers
                                where hm.HouseholdId == id && hm.IsActive
                                join p in _context.Persons on hm.PersonId equals p.Id
                                join u in _context.Users on p.UserId equals u.Id into userGroup
                                from user in userGroup.DefaultIfEmpty()
                                select new HouseholdMemberResponseModel
                                {
                                    Id = hm.Id,
                                    HouseholdId = hm.HouseholdId,
                                    PersonId = hm.PersonId,
                                    PersonName = p.Name,
                                    PersonEmail = user != null ? user.Email : null,
                                    Role = hm.Role,
                                    JoinedDate = hm.JoinedDate ?? hm.CreatedDate,
                                    IsActive = hm.IsActive
                                }).ToListAsync();

            // Get statistics
            // TODO: Update these queries when proper FK relationships are established
            // For now, using navigation properties from household
            var householdWithRelations = await _context.Households
                .Include(h => h.MadeRecipes)
                .Include(h => h.Plans)
                .FirstOrDefaultAsync(h => h.Id == id);

            var recipeCount = householdWithRelations?.MadeRecipes?.Count ?? 0;
            var mealPlanCount = householdWithRelations?.Plans?.Count ?? 0;

            // ShoppingListEntity doesn't have direct navigation from Household
            // Setting to 0 until proper relationship is established
            var shoppingListCount = 0; // TODO: Establish ShoppingList relationship with Household

            return new HouseholdResponseModel
            {
                Id = household.Id,
                Name = household.Name,
                Description = household.Description,
                GroupId = household.GroupId,
                CreatedDate = household.CreatedDate,
                ModifiedDate = household.LastModifiedDate,
                Members = members,
                MemberCount = members.Count,
                RecipeCount = recipeCount,
                MealPlanCount = mealPlanCount,
                ShoppingListCount = shoppingListCount
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

                // Verify the person exists and get their email from Identity User table
                var personWithEmail = await (from p in _context.Persons
                                            where p.Id == model.PersonId
                                            join u in _context.Users on p.UserId equals u.Id into userGroup
                                            from user in userGroup.DefaultIfEmpty()
                                            select new { Person = p, Email = user != null ? user.Email : null })
                                            .FirstOrDefaultAsync();

                if (personWithEmail == null)
                {
                    throw new InvalidOperationException($"Person with ID {model.PersonId} not found");
                }

                // Check if member already exists
                var existingMember = await _context.HouseholdMembers
                    .FirstOrDefaultAsync(hm => hm.HouseholdId == model.HouseholdId && hm.PersonId == model.PersonId);

                if (existingMember != null)
                {
                    throw new InvalidOperationException($"Person {personWithEmail.Person.Name} is already a member of this household");
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
                    PersonName = personWithEmail.Person.Name,
                    PersonEmail = personWithEmail.Email,
                    Role = householdMember.Role,
                    JoinedDate = householdMember.CreatedDate,
                    IsActive = householdMember.IsActive
                };
            }
            catch (Exception ex)
            {
                // Log the error and rethrow
                throw new InvalidOperationException($"Failed to add member to household: {ex.Message}", ex);
            }
        }

        public async Task<bool> RemoveMemberAsync(long householdId, long memberId)
        {
            try
            {
                // Find the household member
                var householdMember = await _context.HouseholdMembers
                    .FirstOrDefaultAsync(hm => hm.HouseholdId == householdId && hm.Id == memberId);

                if (householdMember == null)
                {
                    throw new InvalidOperationException($"Member with ID {memberId} not found in household {householdId}");
                }

                // Remove the member
                _context.HouseholdMembers.Remove(householdMember);
                await _context.SaveChangesAsync();

                return true;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to remove member from household: {ex.Message}", ex);
            }
        }

        public async Task<HouseholdMemberResponseModel> JoinHouseholdAsync(string token, long personId)
        {
            try
            {
                // Find and validate the invite token
                var inviteToken = await _context.HouseholdInviteTokens
                    .Include(t => t.Household)
                    .FirstOrDefaultAsync(t => t.Token == token);

                if (inviteToken == null)
                {
                    throw new InvalidOperationException("Invalid invite token");
                }

                // Check if token is expired
                if (inviteToken.ExpirationDate.HasValue && inviteToken.ExpirationDate.Value < DateTime.UtcNow)
                {
                    throw new InvalidOperationException("Invite token has expired");
                }

                // Check if token has uses left (if limited)
                if (inviteToken.UsesLeft.HasValue && inviteToken.UsesLeft.Value <= 0)
                {
                    throw new InvalidOperationException("Invite token has no uses remaining");
                }

                // Verify the person exists and get their email from Identity User table
                var personWithEmail = await (from p in _context.Persons
                                            where p.Id == personId
                                            join u in _context.Users on p.UserId equals u.Id into userGroup
                                            from user in userGroup.DefaultIfEmpty()
                                            select new { Person = p, Email = user != null ? user.Email : null })
                                            .FirstOrDefaultAsync();

                if (personWithEmail == null)
                {
                    throw new InvalidOperationException($"Person with ID {personId} not found");
                }

                // Check if person is already a member
                var existingMember = await _context.HouseholdMembers
                    .FirstOrDefaultAsync(hm => hm.HouseholdId == inviteToken.HouseholdId && hm.PersonId == personId);

                if (existingMember != null)
                {
                    throw new InvalidOperationException($"Person is already a member of this household");
                }

                // Create the household member
                var householdMember = new HouseholdMemberEntity
                {
                    HouseholdId = inviteToken.HouseholdId,
                    PersonId = personId,
                    Role = "Member",
                    JoinedDate = DateTime.UtcNow,
                    CreatedDate = DateTime.UtcNow,
                    CreatedByPersonId = personId,
                    IsActive = true
                };

                _context.HouseholdMembers.Add(householdMember);

                // Decrement uses left if limited
                if (inviteToken.UsesLeft.HasValue)
                {
                    inviteToken.UsesLeft = inviteToken.UsesLeft.Value - 1;
                }

                await _context.SaveChangesAsync();

                return new HouseholdMemberResponseModel
                {
                    Id = householdMember.Id,
                    HouseholdId = householdMember.HouseholdId,
                    PersonId = householdMember.PersonId,
                    PersonName = personWithEmail.Person.Name,
                    PersonEmail = personWithEmail.Email,
                    Role = householdMember.Role,
                    JoinedDate = householdMember.JoinedDate ?? householdMember.CreatedDate,
                    IsActive = householdMember.IsActive
                };
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to join household: {ex.Message}", ex);
            }
        }
    }
} 
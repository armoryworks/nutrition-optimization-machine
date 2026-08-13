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
            return await GetHouseholdsForMemberAsync(null);
        }

        public async Task<List<HouseholdResponseModel>> GetHouseholdsForMemberAsync(List<long>? householdIds)
        {
            var query = _context.Households.AsQueryable();
            if (householdIds != null)
            {
                if (householdIds.Count == 0) return new List<HouseholdResponseModel>();
                query = query.Where(h => householdIds.Contains(h.Id));
            }

            var households = await query.ToListAsync();
            var ids = households.Select(h => h.Id).ToList();

            // Get member counts per household from HouseholdMembers table
            var memberCounts = await _context.HouseholdMembers
                .Where(hm => hm.IsActive && ids.Contains(hm.HouseholdId))
                .GroupBy(hm => hm.HouseholdId)
                .Select(g => new { HouseholdId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.HouseholdId, x => x.Count);

            // Get meal plan counts per household
            var planCounts = await _context.MealPlans
                .Where(mp => ids.Contains(mp.HouseholdId))
                .GroupBy(mp => mp.HouseholdId)
                .Select(g => new { HouseholdId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.HouseholdId, x => x.Count);

            return households.Select(h => new HouseholdResponseModel
            {
                Id = h.Id,
                Name = h.Name,
                Description = h.Description,
                HouseholdGroupId = h.HouseholdGroupId,
                CreatedDate = h.CreatedDate,
                ModifiedDate = h.LastModifiedDate,
                ManagedBy = h.ManagedBy,
                MemberCount = memberCounts.GetValueOrDefault(h.Id, 0),
                PlanCount = planCounts.GetValueOrDefault(h.Id, 0)
            }).ToList();
        }

        public async Task<HouseholdCreateResponseModel> CreateHouseholdAsync(HouseholdCreateModel model, long? createdByPersonId = null)
        {
            var household = new HouseholdEntity
            {
                Name = model.Name,
                Description = model.Description,
                HouseholdGroupId = model.HouseholdGroupId,
                CreatedDate = DateTime.UtcNow,
                LastModifiedDate = DateTime.UtcNow
            };

            _context.Households.Add(household);
            await _context.SaveChangesAsync();

            // Add the creator as an admin member of the household
            if (createdByPersonId.HasValue)
            {
                var adminMember = new HouseholdMemberEntity
                {
                    HouseholdId = household.Id,
                    PersonId = createdByPersonId.Value,
                    Role = "Admin",
                    JoinedDate = DateTime.UtcNow,
                    CreatedDate = DateTime.UtcNow,
                    CreatedByPersonId = createdByPersonId.Value,
                    IsActive = true,
                    IsAdmin = true,
                    CanManage = true,
                    CanInvite = true
                };
                _context.HouseholdMembers.Add(adminMember);
                await _context.SaveChangesAsync();
            }

            return new HouseholdCreateResponseModel
            {
                Id = household.Id,
                Name = household.Name,
                Description = household.Description,
                HouseholdGroupId = household.HouseholdGroupId,
                CreatedDate = household.CreatedDate
            };
        }

        public async Task<HouseholdResponseModel?> GetHouseholdAsync(long id)
        {
            var household = await _context.Households
                .FirstOrDefaultAsync(h => h.Id == id);

            if (household == null)
                return null;

            // Get household members with person details, email, and profile/restriction status
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
                                    PersonEmail = user != null ? user.Email : p.Email,
                                    Role = hm.Role,
                                    JoinedDate = hm.JoinedDate ?? hm.CreatedDate,
                                    IsActive = hm.IsActive,
                                    HasProfile = _context.PersonAttributes.Any(pa => pa.PersonId == p.Id),
                                    HasRestrictions = _context.Restrictions.Any(r => r.PersonId == p.Id && r.PlanId == null),
                                    IsSteward = hm.IsAdmin || hm.CanManage,
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

            var shoppingListCount = await _context.ShoppingLists.CountAsync(sl => sl.HouseholdId == id);

            return new HouseholdResponseModel
            {
                Id = household.Id,
                Name = household.Name,
                Description = household.Description,
                HouseholdGroupId = household.HouseholdGroupId,
                CreatedDate = household.CreatedDate,
                ModifiedDate = household.LastModifiedDate,
                ManagedBy = household.ManagedBy,
                Members = members,
                MemberCount = members.Count,
                RecipeCount = recipeCount,
                PlanCount = mealPlanCount,
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
            household.HouseholdGroupId = model.HouseholdGroupId ?? household.HouseholdGroupId; // Keep existing value if null
            household.LastModifiedDate = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return new HouseholdResponseModel
            {
                Id = household.Id,
                Name = household.Name,
                Description = household.Description,
                HouseholdGroupId = household.HouseholdGroupId,
                CreatedDate = household.CreatedDate,
                ModifiedDate = household.LastModifiedDate,
                ManagedBy = household.ManagedBy
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

                var personId = householdMember.PersonId;

                // Remove the membership
                _context.HouseholdMembers.Remove(householdMember);
                await _context.SaveChangesAsync();

                // For non-user persons, also clean up the person entity and associated data
                var person = await _context.Persons.FindAsync(personId);
                if (person != null && person.UserId == null)
                {
                    var attributes = await _context.PersonAttributes
                        .Where(pa => pa.PersonId == personId).ToListAsync();
                    _context.PersonAttributes.RemoveRange(attributes);

                    var restrictions = await _context.Restrictions
                        .Where(r => r.PersonId == personId && r.PlanId == null).ToListAsync();
                    _context.Restrictions.RemoveRange(restrictions);

                    _context.Persons.Remove(person);
                    await _context.SaveChangesAsync();
                }

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

                // Managed-enrollment tokens place the household under an
                // external manager on redemption (design doc §5): stamp the
                // marker and emit the handshake event the manager completes.
                // Per-adult consent: policies bind this person only after
                // their own acceptance — recorded manager-side; NOM just
                // reports the join.
                if (inviteToken.Kind == InviteTokenKinds.ManagedEnrollment)
                {
                    if (inviteToken.Household != null && string.IsNullOrEmpty(inviteToken.Household.ManagedBy))
                    {
                        inviteToken.Household.ManagedBy = inviteToken.ManagedBy;
                    }
                    _context.EnrollmentEvents.Add(new EnrollmentEventEntity
                    {
                        HouseholdId = inviteToken.HouseholdId,
                        PersonId = personId,
                        InviteTokenId = inviteToken.Id,
                        EventType = "enrollment_redeemed",
                        ManagedBy = inviteToken.ManagedBy,
                        TemplateRef = inviteToken.TemplateRef,
                        CreatedDate = DateTime.UtcNow,
                        CreatedByPersonId = personId,
                    });
                }
                else if (!string.IsNullOrEmpty(inviteToken.Household?.ManagedBy))
                {
                    // A plain family join into an ALREADY-MANAGED household —
                    // the manager must know, and this adult's consent screen
                    // is triggered manager-side ("invited, not consented").
                    _context.EnrollmentEvents.Add(new EnrollmentEventEntity
                    {
                        HouseholdId = inviteToken.HouseholdId,
                        PersonId = personId,
                        InviteTokenId = inviteToken.Id,
                        EventType = "member_joined_managed",
                        ManagedBy = inviteToken.Household.ManagedBy,
                        CreatedDate = DateTime.UtcNow,
                        CreatedByPersonId = personId,
                    });
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
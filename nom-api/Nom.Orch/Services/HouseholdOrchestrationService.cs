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
        private readonly IPolicyEnforcementService _policy;

        public HouseholdOrchestrationService(ApplicationDbContext context, IPolicyEnforcementService policy)
        {
            _context = context;
            _policy = policy;
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
                IsPersonal = h.IsPersonal,
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
                IsPersonal = model.IsPersonal,
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

            // Tenants that predate the (HouseholdId, PersonId) unique constraint can
            // hold duplicate membership rows; render each person once.
            members = members
                .GroupBy(m => m.PersonId)
                .Select(g => g.OrderByDescending(m => m.IsSteward).ThenBy(m => m.Id).First())
                .ToList();

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
                IsPersonal = household.IsPersonal,
                Members = members,
                MemberCount = members.Count,
                RecipeCount = recipeCount,
                PlanCount = mealPlanCount,
                ShoppingListCount = shoppingListCount
            };
        }

        public async Task<HouseholdEnrollmentInfoModel?> GetEnrollmentInfoAsync(long id)
        {
            var household = await _context.Households
                .Where(h => h.Id == id)
                .Select(h => new { h.ManagedBy })
                .FirstOrDefaultAsync();

            if (household == null)
                return null;

            return new HouseholdEnrollmentInfoModel
            {
                ManagedBy = household.ManagedBy,
                // TODO: Brigade owns provider identity — populate the display
                // name once a provider directory lookup exists.
                ProviderDisplayName = null
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
                ManagedBy = household.ManagedBy,
                IsPersonal = household.IsPersonal
            };
        }

        /// <summary>
        /// Creates a solo user's personal kitchen server-side: the name
        /// ("&lt;FirstName&gt;'s Kitchen", fallback "My Kitchen") and the
        /// IsPersonal flag are never client-supplied. Creator becomes
        /// Admin/steward exactly like a normal household creation.
        /// </summary>
        public async Task<HouseholdCreateResponseModel> CreatePersonalHouseholdAsync(long personId)
        {
            var alreadyInHousehold = await _context.HouseholdMembers
                .AnyAsync(hm => hm.PersonId == personId && hm.IsActive);
            if (alreadyInHousehold)
            {
                throw new InvalidOperationException("already_in_household");
            }

            var personName = await _context.Persons
                .Where(p => p.Id == personId)
                .Select(p => p.Name)
                .FirstOrDefaultAsync();
            var firstName = (personName ?? string.Empty).Trim()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault();
            var kitchenName = string.IsNullOrWhiteSpace(firstName)
                ? "My Kitchen"
                : $"{firstName}'s Kitchen";

            return await CreateHouseholdAsync(new HouseholdCreateModel
            {
                Name = kitchenName,
                HouseholdGroupId = 1,
                IsPersonal = true,
            }, personId);
        }

        /// <summary>
        /// Converts a personal kitchen into a shared household: renames it and
        /// clears the personal flag. Conversion is the side effect of the first
        /// invite — there is no standalone "convert" affordance in the UI.
        /// </summary>
        public async Task<HouseholdResponseModel?> ConvertToSharedAsync(long id, string name, long requesterPersonId)
        {
            if (!await _policy.IsStewardAsync(requesterPersonId, id))
            {
                throw new UnauthorizedAccessException("Only a household steward may convert a personal kitchen.");
            }

            var household = await _context.Households.FindAsync(id);
            if (household == null)
                return null;

            if (!household.IsPersonal)
            {
                throw new InvalidOperationException("not_personal");
            }

            household.Name = name;
            household.IsPersonal = false;
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
                ManagedBy = household.ManagedBy,
                IsPersonal = household.IsPersonal
            };
        }

        /// <summary>
        /// Personal kitchens refuse membership growth (invites, adds, joins)
        /// until converted into a shared household.
        /// </summary>
        private async Task EnsureNotPersonalHouseholdAsync(long householdId)
        {
            if (await _context.Households.AnyAsync(h => h.Id == householdId && h.IsPersonal))
            {
                throw new InvalidOperationException("personal_household");
            }
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
            await EnsureNotPersonalHouseholdAsync(model.HouseholdId);

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
            // Before the wrapping try: the catch below re-wraps messages.
            await EnsureNotPersonalHouseholdAsync(model.HouseholdId);

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
                    if (existingMember.IsActive)
                    {
                        throw new InvalidOperationException($"Person {personWithEmail.Person.Name} is already a member of this household");
                    }
                    existingMember.IsActive = true;
                    existingMember.LastModifiedDate = DateTime.UtcNow;
                    await _context.SaveChangesAsync();
                    return new HouseholdMemberResponseModel
                    {
                        Id = existingMember.Id,
                        HouseholdId = existingMember.HouseholdId,
                        PersonId = existingMember.PersonId,
                        PersonName = personWithEmail.Person.Name,
                        PersonEmail = personWithEmail.Email,
                        Role = existingMember.Role,
                        JoinedDate = existingMember.JoinedDate ?? existingMember.CreatedDate,
                        IsActive = existingMember.IsActive
                    };
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
            // Personal kitchens refuse household_join redemptions, but the
            // MANAGED-ENROLLMENT path stays open: a solo client enrolling
            // with a provider keeps their personal kitchen (no conversion).
            // Before the wrapping try: the catch below re-wraps messages.
            var guardInfo = await _context.HouseholdInviteTokens
                .Where(t => t.Token == token)
                .Select(t => new
                {
                    t.Kind,
                    IsPersonal = t.Household != null && t.Household.IsPersonal,
                })
                .FirstOrDefaultAsync();
            if (guardInfo != null && guardInfo.IsPersonal && guardInfo.Kind != InviteTokenKinds.ManagedEnrollment)
            {
                throw new InvalidOperationException("personal_household");
            }

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

                // Check if person is already a member. For managed_enrollment
                // tokens this is the NORMAL case, not an error: a steward
                // redeeming a provider's token for their EXISTING household
                // (design doc §5, join move 3) is already a member — skip the
                // member insert but still run the enrollment stamping below.
                var existingMember = await _context.HouseholdMembers
                    .FirstOrDefaultAsync(hm => hm.HouseholdId == inviteToken.HouseholdId && hm.PersonId == personId);

                var alreadyMember = existingMember != null;
                if (alreadyMember && inviteToken.Kind != InviteTokenKinds.ManagedEnrollment)
                {
                    // Redeeming the same link twice (double-tap, refreshed tab) is not an
                    // error — the desired state already holds. Reactivate if needed.
                    if (!existingMember!.IsActive)
                    {
                        existingMember.IsActive = true;
                        existingMember.LastModifiedDate = DateTime.UtcNow;
                        await _context.SaveChangesAsync();
                    }
                    return new HouseholdMemberResponseModel
                    {
                        Id = existingMember.Id,
                        HouseholdId = existingMember.HouseholdId,
                        PersonId = existingMember.PersonId,
                        PersonName = personWithEmail.Person.Name,
                        PersonEmail = personWithEmail.Email,
                        Role = existingMember.Role,
                        JoinedDate = existingMember.JoinedDate ?? existingMember.CreatedDate,
                        IsActive = existingMember.IsActive
                    };
                }

                // Create the household member (skipped when an enrollment
                // redemption comes from an existing member — see above).
                var householdMember = existingMember ?? new HouseholdMemberEntity
                {
                    HouseholdId = inviteToken.HouseholdId,
                    PersonId = personId,
                    Role = "Member",
                    JoinedDate = DateTime.UtcNow,
                    CreatedDate = DateTime.UtcNow,
                    CreatedByPersonId = personId,
                    IsActive = true
                };

                if (!alreadyMember)
                {
                    _context.HouseholdMembers.Add(householdMember);
                }

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
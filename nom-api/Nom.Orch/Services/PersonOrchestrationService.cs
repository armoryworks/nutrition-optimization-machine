// File: Nom.Orch/Services/PersonOrchestrationService.cs

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Nom.Data;
using Nom.Data.Person;
using Nom.Data.Plan;
using Nom.Data.Reference;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Person;
using Nom.Orch.Models.Privacy;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Nom.Orch.Services
{
    /// <summary>
    /// Implements the business logic for Person orchestration,
    /// managing the lifecycle and initial setup of Person entities.
    /// </summary>
    public class PersonOrchestrationService : IPersonOrchestrationService
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IPrivacyOrchestrationService _privacyOrchestrationService;
        private readonly IHouseholdOrchestrationService _householdOrchestrationService;
        private readonly ILogger<PersonOrchestrationService> _logger;

        private readonly ICurrentUserService _currentUser;

        public PersonOrchestrationService(
            ApplicationDbContext dbContext,
            IHttpContextAccessor httpContextAccessor,
            ICurrentUserService currentUser,
            IPrivacyOrchestrationService privacyOrchestrationService,
            IHouseholdOrchestrationService householdOrchestrationService,
            ILogger<PersonOrchestrationService> logger)
        {
            _dbContext = dbContext;
            _httpContextAccessor = httpContextAccessor;
            _currentUser = currentUser;
            _privacyOrchestrationService = privacyOrchestrationService;
            _householdOrchestrationService = householdOrchestrationService;
            _logger = logger;
        }

        /// <summary>
        /// Creates a new person if one does not already exist for the current user,
        /// otherwise updates the existing person's name.
        /// </summary>
        public async Task<PersonCreateResponseModel> UpsertPersonAsync(PersonCreateModel request)
        {
            _logger.LogInformation("Upserting person with name {Name}", request.PersonName);

            var userId = _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                throw new InvalidOperationException("User is not authenticated.");
            }

            var existingPerson = await _dbContext.Persons
                .FirstOrDefaultAsync(p => p.UserId == userId);

            if (existingPerson != null)
            {
                existingPerson.Name = request.PersonName;
                _dbContext.Persons.Update(existingPerson);
                await _dbContext.SaveChangesAsync();

                _logger.LogInformation("Successfully updated person {PersonId}", existingPerson.Id);
                return new PersonCreateResponseModel
                {
                    Id = existingPerson.Id,
                    Name = existingPerson.Name,
                    UserId = existingPerson.UserId
                };
            }

            var newPerson = new PersonEntity
            {
                Name = request.PersonName,
                UserId = userId,
                CreatedByPersonId = SystemConstants.SystemPersonId
            };

            _dbContext.Persons.Add(newPerson);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Successfully created person {PersonId}", newPerson.Id);
            return new PersonCreateResponseModel
            {
                Id = newPerson.Id,
                Name = newPerson.Name,
                UserId = newPerson.UserId
            };
        }

        /// <summary>
        /// Sets up a new Person entity after successful user registration.
        /// Creates a Person record, generates an invitation code, and saves to the database.
        /// </summary>
        /// <param name="identityUserId">The ID of the IdentityUser linked to this person.</param>
        /// <param name="personName">The initial name for the person.</param>
        /// <returns>The newly created PersonEntity.</returns>
        public async Task<PersonEntity> SetupNewRegisteredPersonAsync(string identityUserId, string personName)
        {
            var systemPerson = await _dbContext.Persons.FirstOrDefaultAsync(p => p.Name == "System");
            if (systemPerson == null)
            {
                systemPerson = new PersonEntity
                {
                    Name = "System",
                    UserId = null,
                };
                _dbContext.Persons.Add(systemPerson);
                await _dbContext.SaveChangesAsync();
            }

            var newPerson = new PersonEntity
            {
                Name = personName,
                UserId = identityUserId,
            };

            _dbContext.Persons.Add(newPerson);
            await _dbContext.SaveChangesAsync();

            return newPerson;
        }

        /// <summary>
        /// Handles the complete onboarding process for a user, including creating/updating their
        /// Person details, attributes, restrictions, and initial consents.
        /// </summary>
        /// <param name="request">Consolidated onboarding data from the frontend.</param>
        /// <returns>An OnboardingCompleteResponse indicating success and the primary PersonId.</returns>
        public async Task<OnboardingCompleteResponse> CompleteOnboardingAsync(OnboardingCompleteRequest request)
        {
            _logger.LogInformation("Completing onboarding for person {PersonId}", request.PersonId);

            // During onboarding, we work with the person directly rather than requiring user authentication
            PersonEntity primaryPerson;
            
            if (request.PersonId.HasValue && request.PersonId.Value > 0)
            {
                // Use existing person
                primaryPerson = await _dbContext.Persons.FindAsync(request.PersonId.Value);
                
            }
            else
            {
                // Create new person from details
                primaryPerson = new PersonEntity
                {
                    Name = request.PersonDetails.Name,
                    CreatedDate = DateTime.UtcNow
                };
                _dbContext.Persons.Add(primaryPerson);
                await _dbContext.SaveChangesAsync();
            }



            // Solo "personal kitchen" fork: silently auto-create a household
            // of one. The creator becomes Admin/steward exactly like a normal
            // household creation; the flag only suppresses membership growth
            // until the household is converted by the first invite.
            if (request.CreateSoloHousehold)
            {
                var alreadyInHousehold = await _dbContext.HouseholdMembers
                    .AnyAsync(hm => hm.PersonId == primaryPerson.Id && hm.IsActive);
                if (!alreadyInHousehold)
                {
                    var firstName = (primaryPerson.Name ?? string.Empty).Trim()
                        .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                        .FirstOrDefault();
                    var kitchenName = string.IsNullOrWhiteSpace(firstName)
                        ? "My Kitchen"
                        : $"{firstName}'s Kitchen";

                    await _householdOrchestrationService.CreateHouseholdAsync(new Models.Household.HouseholdCreateModel
                    {
                        Name = kitchenName,
                        HouseholdGroupId = 1,
                        IsPersonal = true,
                    }, primaryPerson.Id);

                    _logger.LogInformation("Created personal kitchen for person {PersonId}", primaryPerson.Id);
                }
            }

            // Handle plan invitation if provided
            if (!string.IsNullOrWhiteSpace(request.PlanInvitationCode))
            {
                var invitation = await _dbContext.Invitations
                    .FirstOrDefaultAsync(i => i.Code == request.PlanInvitationCode && i.IsUsed != true);

                if (invitation != null)
                {
                    if (invitation.PlanId.HasValue)
                    {
                        // Add person to the plan
                        var planMember = new PlanParticipantEntity
                        {
                            PlanId = invitation.PlanId.Value,
                            PersonId = primaryPerson.Id,
                            RoleRefId = 4101L, // Member role
                            JoinedDate = DateTime.UtcNow
                        };
                        _dbContext.PlanParticipants.Add(planMember);
                    }
                    // Mark invitation as used
                    invitation.IsUsed = true;
                    invitation.UsedAt = DateTime.UtcNow;

                    _logger.LogInformation("Successfully claimed plan invitation for person {PersonId}", primaryPerson.Id);
                }
                else
                {
                    _logger.LogWarning("Invalid or expired plan invitation code: {Code}", request.PlanInvitationCode);
                }
            }

            // Create default plan for the primary person
            var defaultPlan = new PlanEntity
            {
                Name = $"{primaryPerson.Name}'s Default Plan",
                AuthorId = primaryPerson.Id,
                CurationStatusId = 9000L, // NonCurated
                Version = 1
            };
            _dbContext.Plans.Add(defaultPlan);
            await _dbContext.SaveChangesAsync();

            // Add primary person as admin to their default plan
            var primaryParticipant = new PlanParticipantEntity
            {
                PlanId = defaultPlan.Id,
                PersonId = primaryPerson.Id,
                RoleRefId = 4100L, // Admin role
                JoinedDate = DateTime.UtcNow,
                IsAdmin = true,
                CanManage = true,
                CanInvite = true
            };
            _dbContext.PlanParticipants.Add(primaryParticipant);

            // Add additional participants if provided
            var additionalParticipantPersonIds = new Dictionary<long, long>(); // Maps TempId to actual PersonId
            if (request.HasAdditionalParticipants && request.AdditionalParticipantDetails != null && request.AdditionalParticipantDetails.Any())
            {
                foreach (var participantDetails in request.AdditionalParticipantDetails)
                {
                    long actualPersonId;

                    // Check if this is a pre-created dependent (Id > 0 and owned by the primary person)
                    if (participantDetails.Id > 0)
                    {
                        var existingPerson = await _dbContext.Persons.FindAsync(participantDetails.Id);
                        if (existingPerson != null && existingPerson.CreatedByPersonId == primaryPerson.Id)
                        {
                            actualPersonId = existingPerson.Id;
                        }
                        else
                        {
                            // Not a valid pre-created dependent — create new
                            var newParticipant = new PersonEntity
                            {
                                Name = participantDetails.Name,
                                CreatedByPersonId = primaryPerson.Id,
                                CreatedDate = DateTime.UtcNow
                            };
                            _dbContext.Persons.Add(newParticipant);
                            await _dbContext.SaveChangesAsync();
                            actualPersonId = newParticipant.Id;
                        }
                    }
                    else
                    {
                        // TempId — create new person (original behavior)
                        var newParticipant = new PersonEntity
                        {
                            Name = participantDetails.Name,
                            CreatedByPersonId = primaryPerson.Id,
                            CreatedDate = DateTime.UtcNow
                        };
                        _dbContext.Persons.Add(newParticipant);
                        await _dbContext.SaveChangesAsync();
                        actualPersonId = newParticipant.Id;
                    }

                    additionalParticipantPersonIds[participantDetails.Id] = actualPersonId;

                    // Add to plan as participant (avoid duplicates)
                    var alreadyParticipant = await _dbContext.PlanParticipants
                        .AnyAsync(pp => pp.PlanId == defaultPlan.Id && pp.PersonId == actualPersonId);
                    if (!alreadyParticipant)
                    {
                        _dbContext.PlanParticipants.Add(new PlanParticipantEntity
                        {
                            PlanId = defaultPlan.Id,
                            PersonId = actualPersonId,
                            RoleRefId = 4101L, // Member role
                            JoinedDate = DateTime.UtcNow
                        });
                    }

                    // Migrate person-level restrictions to the plan
                    var dependentRestrictions = await _dbContext.Restrictions
                        .Where(r => r.PersonId == actualPersonId && r.PlanId == null)
                        .ToListAsync();
                    foreach (var r in dependentRestrictions)
                    {
                        r.PlanId = defaultPlan.Id;
                    }
                }
            }

            // Persist primary person attributes
            if (request.Attributes != null && request.Attributes.Any())
            {
                foreach (var attr in request.Attributes)
                {
                    var personAttribute = new PersonAttributeEntity
                    {
                        PersonId = primaryPerson.Id,
                        AttributeTypeId = attr.AttributeTypeRefId,
                        Value = attr.Value,
                        CreatedDate = DateTime.UtcNow,
                        CreatedByPersonId = primaryPerson.Id
                    };
                    _dbContext.PersonAttributes.Add(personAttribute);
                }
            }

            // Persist additional participant attributes
            if (request.AdditionalParticipantDetails != null && request.AdditionalParticipantDetails.Any())
            {
                foreach (var detail in request.AdditionalParticipantDetails)
                {
                    if (additionalParticipantPersonIds.ContainsKey(detail.Id) && detail.Attributes != null && detail.Attributes.Any())
                    {
                        var actualPersonId = additionalParticipantPersonIds[detail.Id];
                        foreach (var attr in detail.Attributes)
                        {
                            var personAttribute = new PersonAttributeEntity
                            {
                                PersonId = actualPersonId,
                                AttributeTypeId = attr.AttributeTypeRefId,
                                Value = attr.Value,
                                CreatedDate = DateTime.UtcNow,
                                CreatedByPersonId = primaryPerson.Id
                            };
                            _dbContext.PersonAttributes.Add(personAttribute);
                        }
                    }
                }
            }

            // Clean up any person-level restrictions saved during onboarding (before plan existed)
            var personLevelRestrictions = await _dbContext.Restrictions
                .Where(r => r.PersonId == primaryPerson.Id && r.PlanId == null)
                .ToListAsync();
            _dbContext.Restrictions.RemoveRange(personLevelRestrictions);

            // Persist restrictions to the plan
            if (request.Restrictions != null && request.Restrictions.Any())
            {
                foreach (var restriction in request.Restrictions)
                {
                    if (restriction.AppliesToEntirePlan)
                    {
                        // Create a single restriction for the entire plan
                        var restrictionEntity = new RestrictionEntity
                        {
                            PlanId = defaultPlan.Id,
                            PersonId = null, // Plan-wide restriction
                            Name = restriction.Name,
                            Description = restriction.Description,
                            RestrictionTypeId = restriction.RestrictionTypeId,
                            CreatedDate = DateTime.UtcNow,
                            CreatedByPersonId = primaryPerson.Id
                        };
                        _dbContext.Restrictions.Add(restrictionEntity);
                    }
                    else if (restriction.AffectedPersonIds != null && restriction.AffectedPersonIds.Any())
                    {
                        // Create individual restrictions for each affected person
                        foreach (var personId in restriction.AffectedPersonIds)
                        {
                            var restrictionEntity = new RestrictionEntity
                            {
                                PlanId = defaultPlan.Id,
                                PersonId = personId,
                                Name = restriction.Name,
                                Description = restriction.Description,
                                RestrictionTypeId = restriction.RestrictionTypeId,
                                CreatedDate = DateTime.UtcNow,
                                CreatedByPersonId = primaryPerson.Id
                            };
                            _dbContext.Restrictions.Add(restrictionEntity);
                        }
                    }
                }
            }

            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Successfully completed onboarding for person {PersonId}", primaryPerson.Id);
            return new OnboardingCompleteResponse
            {
                Success = true,
                Message = "Onboarding completed successfully",
                PersonId = primaryPerson.Id
            };
        }

        /// <summary>
        /// Gets the onboarding state for a specific person by their ID.
        /// </summary>
        public async Task<OnboardingStateResponse> GetOnboardingStateAsync(long personId)
        {
            var person = await _dbContext.Persons.FindAsync(personId)
                ?? throw new KeyNotFoundException($"Person with ID {personId} not found.");

            var response = new OnboardingStateResponse
            {
                HasExistingPerson = true,
                PersonId = person.Id,
                PersonDetails = new PersonDetailsRequest
                {
                    Id = person.Id,
                    Name = person.Name ?? string.Empty
                }
            };

            // Get existing attributes
            var existingAttributes = await _dbContext.PersonAttributes
                .Where(pa => pa.PersonId == person.Id)
                .ToListAsync();

            response.Attributes = existingAttributes.Select(pa => new PersonAttributeRequest
            {
                AttributeTypeRefId = pa.AttributeTypeId,
                Value = pa.Value ?? string.Empty
            }).ToList();

            // Get existing restrictions
            var existingRestrictions = await _dbContext.Restrictions
                .Where(r => r.PersonId == person.Id)
                .ToListAsync();

            response.Restrictions = existingRestrictions.Select(r => new RestrictionRequest
            {
                Id = r.Id,
                Name = r.Name ?? string.Empty,
                Description = r.Description,
                RestrictionTypeId = r.RestrictionTypeId ?? 0,
                AppliesToEntirePlan = false,
                AffectedPersonIds = new List<long>(),
                Locked = r.Locked,
                LockedBy = r.LockedBy
            }).ToList();

            // Check household membership
            var hasHousehold = await _dbContext.HouseholdMembers
                .AnyAsync(hm => hm.PersonId == person.Id && hm.IsActive);
            response.HasHousehold = hasHousehold;

            // Check if person is already part of a plan
            var planParticipation = await _dbContext.PlanParticipants
                .FirstOrDefaultAsync(pp => pp.PersonId == person.Id);

            if (planParticipation != null)
            {
                response.IsComplete = true;
                response.CurrentStep = 4; // All steps complete
            }
            else
            {
                response.IsComplete = false;
                // Infer current wizard step from saved data
                // Step 0: Profile, Step 1: Restrictions, Step 2: Household, Step 3: Plan
                int step = 0;
                if (existingAttributes.Any()) step = 1;
                if (existingRestrictions.Any()) step = 2;
                if (hasHousehold) step = 3;
                response.CurrentStep = step;
            }

            return response;
        }

        /// <summary>
        /// Retrieves the current PersonId from the authenticated user's claims.
        /// Returns null if the user is in registration phase and doesn't have a PersonId yet.
        /// </summary>
        public long? GetCurrentPersonId() => _currentUser.PersonId;

        /// <summary>
        /// Retrieves the current PersonId from the authenticated user's claims.
        /// Throws UnauthorizedAccessException if PersonId is not available.
        /// Use this method only for endpoints that require a complete user profile.
        /// </summary>
        public long GetCurrentPersonIdRequired()
        {
            var personId = GetCurrentPersonId();
            if (personId.HasValue)
            {
                return personId.Value;
            }

            throw new UnauthorizedAccessException("PersonId claim is missing, invalid, or could not be parsed from the user's token.");
        }

        public async Task<PersonModel> GetPersonByUserIdAsync(string userId)
        {
            var person = await _dbContext.Persons
                .Include(p => p.PlanParticipations)
                .ThenInclude(pp => pp.Plan)
                .FirstOrDefaultAsync(p => p.UserId == userId);

            return person != null ? await GetPersonModelAsync(person.Id) : null!;
        }

        public async Task<PersonModel> GetPersonByIdAsync(long personId)
        {
            return await GetPersonModelAsync(personId);
        }

        public async Task<List<PersonModel>> GetAllPersonsAsync()
        {
            var persons = await _dbContext.Persons
                .OrderByDescending(p => p.CreatedDate)
                .ToListAsync();

            var personModels = new List<PersonModel>();
            foreach (var person in persons)
            {
                personModels.Add(await GetPersonModelAsync(person.Id));
            }

            return personModels;
        }

        public async Task<List<PersonModel>> GetPersonsForHouseholdsAsync(List<long> householdIds)
        {
            if (householdIds.Count == 0) return new List<PersonModel>();

            var personIds = await _dbContext.HouseholdMembers
                .Where(hm => hm.IsActive && householdIds.Contains(hm.HouseholdId))
                .Select(hm => hm.PersonId)
                .Distinct()
                .ToListAsync();

            var personModels = new List<PersonModel>();
            foreach (var personId in personIds)
            {
                personModels.Add(await GetPersonModelAsync(personId));
            }

            return personModels;
        }

        public async Task<List<PersonModel>> GetPersonsByPlanIdAsync(long planId)
        {
            var participants = await _dbContext.PlanParticipants
                // .Include(pp => pp.Person)
                // .Include(pp => pp.Plan)
                .Where(pp => pp.PlanId == planId)
                .ToListAsync();

            var personModels = new List<PersonModel>();
            foreach (var participant in participants)
            {
                personModels.Add(await GetPersonModelAsync(participant.PersonId));
            }

            return personModels;
        }

        public async Task<PersonModel> UpdatePersonAsync(UpdatePersonRequest request)
        {
            var person = await _dbContext.Persons.FindAsync(request.Id);
            if (person == null)
                throw new KeyNotFoundException($"Person with ID {request.Id} not found.");

            person.Name = request.Name;
            person.UserId = request.UserId;

            _dbContext.Persons.Update(person);
            await _dbContext.SaveChangesAsync();

            return await GetPersonModelAsync(person.Id);
        }

        public async Task<bool> IsPersonInHouseholdsAsync(long personId, List<long> householdIds)
        {
            if (householdIds.Count == 0) return false;
            return await _dbContext.HouseholdMembers
                .AnyAsync(hm => hm.PersonId == personId && hm.IsActive && householdIds.Contains(hm.HouseholdId));
        }

        public async Task<List<PersonModel>> SearchPersonsAsync(string query, int limit = 20)
        {
            var persons = await _dbContext.Persons
                .Where(p => EF.Functions.ILike(p.Name, $"%{query}%"))
                .OrderBy(p => p.Name)
                .Take(limit)
                .ToListAsync();

            var results = new List<PersonModel>();
            foreach (var person in persons)
            {
                results.Add(await GetPersonModelAsync(person.Id));
            }
            return results;
        }

        public async Task<bool> DeletePersonAsync(long personId)
        {
            var person = await _dbContext.Persons.FindAsync(personId);
            if (person == null)
                return false;

            _dbContext.Persons.Remove(person);
            await _dbContext.SaveChangesAsync();
            return true;
        }

        private async Task<PersonModel> GetPersonModelAsync(long personId)
        {
            var person = await _dbContext.Persons
                .Include(p => p.PlanParticipations)
                .ThenInclude(pp => pp.Plan)
                .FirstOrDefaultAsync(p => p.Id == personId);

            if (person == null)
                throw new KeyNotFoundException($"Person with ID {personId} not found.");

            var planParticipations = person.PlanParticipations?.Select(pp => new PlanParticipantModel
            {
                Id = pp.Id,
                PlanId = pp.PlanId,
                PlanName = pp.Plan?.Name ?? "Unknown",
                PersonId = pp.PersonId,
                PersonName = person.Name,
                RoleId = pp.RoleRefId,
                RoleName = GetRoleName(pp.RoleRefId),
                IsActive = true // Plan participants are always active
            }).ToList() ?? new List<PlanParticipantModel>();

            return new PersonModel
            {
                Id = person.Id,
                Name = person.Name,
                UserId = person.UserId,
                CreatedDate = person.CreatedDate,
                CreatedByPersonId = person.CreatedByPersonId,
                Attributes = await GetPersonAttributesAsync(personId),
                PlanParticipations = planParticipations
            };
        }

        private string GetRoleName(long roleRefId)
        {
            return roleRefId switch
            {
                4100L => "Admin",
                4101L => "Member",
                4102L => "Viewer",
                _ => "Unknown"
            };
        }

        /// <summary>
        /// Saves a person's profile (name + attributes).
        /// If personId is 0, creates a new non-user person (optionally adding to a household).
        /// If personId > 0, updates the existing person. Replaces all existing attributes.
        /// </summary>
        public async Task<PersonModel> SaveProfileAsync(long personId, SaveProfileRequest request)
        {
            PersonEntity person;
            long currentPersonId;

            if (personId == 0)
            {
                // Create a new non-user person
                currentPersonId = GetCurrentPersonIdRequired();

                // Validate household membership if specified
                if (request.HouseholdId.HasValue)
                {
                    var isMember = await _dbContext.HouseholdMembers
                        .AnyAsync(hm => hm.PersonId == currentPersonId
                                     && hm.HouseholdId == request.HouseholdId.Value
                                     && hm.IsActive);
                    if (!isMember)
                    {
                        throw new UnauthorizedAccessException("You are not a member of the specified household.");
                    }
                }

                person = new PersonEntity
                {
                    Name = request.Name,
                    Email = request.Email,
                    UserId = null,
                    CreatedByPersonId = currentPersonId,
                    CreatedDate = DateTime.UtcNow,
                };
                _dbContext.Persons.Add(person);
                await _dbContext.SaveChangesAsync();

                // Add to household if specified
                if (request.HouseholdId.HasValue)
                {
                    // Personal kitchens refuse membership growth until converted.
                    if (await _dbContext.Households.AnyAsync(h => h.Id == request.HouseholdId.Value && h.IsPersonal))
                    {
                        throw new InvalidOperationException("personal_household");
                    }

                    _dbContext.HouseholdMembers.Add(new HouseholdMemberEntity
                    {
                        HouseholdId = request.HouseholdId.Value,
                        PersonId = person.Id,
                        Role = "Member",
                        JoinedDate = DateTime.UtcNow,
                        CreatedDate = DateTime.UtcNow,
                        CreatedByPersonId = currentPersonId,
                        IsActive = true,
                    });
                    await _dbContext.SaveChangesAsync();
                }

                _logger.LogInformation("Created non-user person {PersonId} for creator {CreatorId}", person.Id, currentPersonId);
            }
            else
            {
                // Update existing person
                person = await _dbContext.Persons.FindAsync(personId)
                    ?? throw new KeyNotFoundException($"Person with ID {personId} not found.");

                person.Name = request.Name;
                if (request.Email != null)
                {
                    person.Email = request.Email;
                }
                _dbContext.Persons.Update(person);
                currentPersonId = person.Id;

                // Remove all existing attributes for this person
                var oldAttributes = await _dbContext.PersonAttributes
                    .Where(pa => pa.PersonId == person.Id)
                    .ToListAsync();
                _dbContext.PersonAttributes.RemoveRange(oldAttributes);
            }

            // Add new attributes
            if (request.Attributes != null)
            {
                foreach (var attr in request.Attributes)
                {
                    _dbContext.PersonAttributes.Add(new PersonAttributeEntity
                    {
                        PersonId = person.Id,
                        AttributeTypeId = attr.AttributeTypeRefId,
                        Value = attr.Value,
                        CreatedDate = DateTime.UtcNow,
                        CreatedByPersonId = currentPersonId
                    });
                }
            }

            await _dbContext.SaveChangesAsync();
            _logger.LogInformation("Saved profile for person {PersonId}", person.Id);

            return await GetPersonModelAsync(person.Id);
        }

        public async Task SaveRestrictionsAsync(long personId, List<RestrictionRequest> restrictions)
        {
            var person = await _dbContext.Persons.FindAsync(personId)
                ?? throw new KeyNotFoundException($"Person with ID {personId} not found.");

            // Remove existing person-level restrictions (those without a plan) —
            // EXCEPT locked ones. A locked restriction (set by a household
            // steward or an external manager) survives profile saves and can
            // only be changed through the steward/manager path. Silently
            // preserving (rather than erroring) keeps the profile page usable:
            // the UI renders locked rows read-only alongside editable ones.
            var oldRestrictions = await _dbContext.Restrictions
                .Where(r => r.PersonId == person.Id && r.PlanId == null && !r.Locked)
                .ToListAsync();
            _dbContext.Restrictions.RemoveRange(oldRestrictions);

            var lockedNames = await _dbContext.Restrictions
                .Where(r => r.PersonId == person.Id && r.PlanId == null && r.Locked)
                .Select(r => r.Name)
                .ToListAsync();

            // Add new restrictions at the person level (no plan yet)
            if (restrictions != null)
            {
                foreach (var restriction in restrictions)
                {
                    // Don't duplicate a locked restriction the member re-submits.
                    if (lockedNames.Contains(restriction.Name, StringComparer.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    _dbContext.Restrictions.Add(new RestrictionEntity
                    {
                        PersonId = person.Id,
                        PlanId = null,
                        Name = restriction.Name,
                        Description = restriction.Description,
                        RestrictionTypeId = restriction.RestrictionTypeId,
                        CreatedDate = DateTime.UtcNow,
                        CreatedByPersonId = person.Id
                    });
                }
            }

            await _dbContext.SaveChangesAsync();
            _logger.LogInformation("Saved {Count} restrictions for person {PersonId}", restrictions?.Count ?? 0, person.Id);
        }

        private async Task<List<PersonAttributeModel>> GetPersonAttributesAsync(long personId)
        {
            var attributes = await _dbContext.PersonAttributes
                .Where(pa => pa.PersonId == personId)
                .Select(pa => new PersonAttributeModel
                {
                    Id = pa.Id,
                    PersonId = pa.PersonId,
                    AttributeTypeId = pa.AttributeTypeId,
                    Value = pa.Value,
                    AttributeTypeName = pa.AttributeType != null ? pa.AttributeType.Name : "Unknown"
                })
                .ToListAsync();

            return attributes;
        }
    }
}

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
        private readonly ILogger<PersonOrchestrationService> _logger;

        public PersonOrchestrationService(
            ApplicationDbContext dbContext,
            IHttpContextAccessor httpContextAccessor,
            IPrivacyOrchestrationService privacyOrchestrationService,
            ILogger<PersonOrchestrationService> logger)
        {
            _dbContext = dbContext;
            _httpContextAccessor = httpContextAccessor;
            _privacyOrchestrationService = privacyOrchestrationService;
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
                CreatedByPersonId = 1L // System person
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
                JoinedDate = DateTime.UtcNow
            };
            _dbContext.PlanParticipants.Add(primaryParticipant);

            // Add additional participants if provided
            if (request.HasAdditionalParticipants && request.AdditionalParticipantDetails != null && request.AdditionalParticipantDetails.Any())
            {
                foreach (var participantDetails in request.AdditionalParticipantDetails)
                {
                    var newParticipant = new PersonEntity
                    {
                        Name = participantDetails.Name,
                        CreatedByPersonId = primaryPerson.Id,
                        CreatedDate = DateTime.UtcNow
                    };
                    _dbContext.Persons.Add(newParticipant);
                    await _dbContext.SaveChangesAsync();

                    var participant = new PlanParticipantEntity
                    {
                        PlanId = defaultPlan.Id,
                        PersonId = newParticipant.Id,
                        RoleRefId = 4101L, // Member role
                        JoinedDate = DateTime.UtcNow
                    };
                    _dbContext.PlanParticipants.Add(participant);
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
        /// Gets the current onboarding state for a user, including existing person data
        /// </summary>
        public async Task<OnboardingStateResponse> GetOnboardingStateAsync(string? userId = null)
        {
            try
            {
                var response = new OnboardingStateResponse();
                
                // Try to get existing person data
                PersonEntity? existingPerson = null;
                
                if (!string.IsNullOrEmpty(userId))
                {
                    // If userId provided, try to find person by userId
                    existingPerson = await _dbContext.Persons.FirstOrDefaultAsync(p => p.UserId == userId);
                }
                else
                {
                    // Try to get from current authenticated user
                    try
                    {
                        var currentUserId = GetCurrentUserId();
                        if (!string.IsNullOrEmpty(currentUserId))
                        {
                            existingPerson = await _dbContext.Persons.FirstOrDefaultAsync(p => p.UserId == currentUserId);
                        }
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // User not authenticated, which is fine for onboarding
                        _logger.LogInformation("User not authenticated during onboarding state fetch");
                    }
                }

                if (existingPerson != null)
                {
                    response.HasExistingPerson = true;
                    response.PersonId = existingPerson.Id;
                    response.PersonDetails = new PersonDetailsRequest
                    {
                        Id = existingPerson.Id,
                        Name = existingPerson.Name ?? string.Empty
                    };

                    // Get existing attributes
                    var existingAttributes = await _dbContext.PersonAttributes
                        .Where(pa => pa.PersonId == existingPerson.Id)
                        .ToListAsync();

                    response.Attributes = existingAttributes.Select(pa => new PersonAttributeRequest
                    {
                        AttributeTypeRefId = pa.AttributeTypeId,
                        Value = pa.Value ?? string.Empty
                    }).ToList();

                    // Get existing restrictions
                    var existingRestrictions = await _dbContext.Restrictions
                        .Where(r => r.PersonId == existingPerson.Id)
                        .ToListAsync();

                    response.Restrictions = existingRestrictions.Select(r => new RestrictionRequest
                    {
                        Name = r.Name ?? string.Empty,
                        Description = r.Description,
                        RestrictionTypeId = r.RestrictionTypeId ?? 0,
                        AppliesToEntirePlan = false, // Default value since this property doesn't exist in entity
                        AffectedPersonIds = new List<long>() // Default empty list since this property doesn't exist in entity
                    }).ToList();

                    // Check if person is already part of a plan
                    var planParticipation = await _dbContext.PlanParticipants
                        .FirstOrDefaultAsync(pp => pp.PersonId == existingPerson.Id);

                    if (planParticipation != null)
                    {
                        response.IsComplete = true;
                        response.CurrentStep = 100; // Completed
                    }
                    else
                    {
                        response.IsComplete = false;
                        response.CurrentStep = 50; // Person created but no plan yet
                    }
                }
                else
                {
                    response.HasExistingPerson = false;
                    response.IsComplete = false;
                    response.CurrentStep = 0; // Not started
                }

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching onboarding state");
                throw;
            }
        }

        private string? GetCurrentUserId()
        {
            var userId = _httpContextAccessor.HttpContext?.User?.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId) || !long.TryParse(userId, out var id))
            {
                throw new UnauthorizedAccessException("User not authenticated");
            }
            return userId;
        }

        /// <summary>
        /// Retrieves the current PersonId from the authenticated user's claims.
        /// Returns null if the user is in registration phase and doesn't have a PersonId yet.
        /// </summary>
        public long? GetCurrentPersonId()
        {
            var personIdClaim = _httpContextAccessor.HttpContext?.User?.Claims?.FirstOrDefault(c => c.Type == "PersonId")?.Value;
            if (long.TryParse(personIdClaim, out long personId))
            {
                return personId;
            }
            return null;
        }

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

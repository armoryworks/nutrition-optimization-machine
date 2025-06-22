// Nom.Orch/Services/PersonOrchestrationService.cs
using Nom.Orch.Interfaces;
using Nom.Data; // For ApplicationDbContext
using Nom.Data.Person; // For PersonEntity
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore; // For ToListAsync() and FirstOrDefaultAsync()
using Microsoft.AspNetCore.Http;
using Nom.Data.Plan;
using Nom.Data.Reference;
using Nom.Orch.Models.Person; // For OnboardingCompleteRequest and OnboardingCompleteResponse
using System.Collections.Generic; // For List and Dictionary
using System.Security.Claims; // For Claims

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
        private readonly IRestrictionOrchestrationService _restrictionOrchestrationService;

        public PersonOrchestrationService(ApplicationDbContext dbContext,
            IRestrictionOrchestrationService restrictionOrchestrationService,
            IHttpContextAccessor httpContextAccessor)
        {
            _dbContext = dbContext;
            _httpContextAccessor = httpContextAccessor;
            _restrictionOrchestrationService = restrictionOrchestrationService;
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
            // Ensure the "System" person exists
            var systemPerson = await _dbContext.Persons.FirstOrDefaultAsync(p => p.Name == "System");
            if (systemPerson == null)
            {
                systemPerson = new PersonEntity
                {
                    Name = "System",
                    UserId = null, // System person is not linked to an IdentityUser
                    InvitationCode = null
                    // Audit fields will be set by ApplicationDbContext
                };
                _dbContext.Persons.Add(systemPerson);
                await _dbContext.SaveChangesAsync();
            }

            // Generate a unique invitation code for the new person
            var invitationCode = await GenerateUniqueInvitationCodeAsync();

            var newPerson = new PersonEntity
            {
                Name = personName,
                UserId = identityUserId, // Link to AspNetUser
                InvitationCode = invitationCode
                // Audit fields will be set by ApplicationDbContext
            };

            // Add the new person to the database
            _dbContext.Persons.Add(newPerson);
            await _dbContext.SaveChangesAsync();

            return newPerson;
        }

        /// <summary>
        /// Generates a unique 6-character alphanumeric invitation code.
        /// </summary>
        /// <returns>A unique invitation code string.</returns>
        public async Task<string> GenerateUniqueInvitationCodeAsync()
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            var random = new Random();
            string code;
            bool isUnique;

            do
            {
                code = new string(Enumerable.Repeat(chars, 6)
                  .Select(s => s[random.Next(s.Length)]).ToArray());

                // Check if the code already exists in the database
                isUnique = !await _dbContext.Persons.AnyAsync(p => p.InvitationCode == code) &&
                           !await _dbContext.Plans.AnyAsync(p => p.InvitationCode == code); // Check Plan invitation codes too

            } while (!isUnique);

            return code;
        }

        /// <summary>
        /// Handles the complete onboarding process for a user, including creating/updating their
        /// Person details, associated attributes, and restrictions for all participants.
        /// </summary>
        /// <param name="request">Consolidated onboarding data from the frontend.</param>
        /// <param name="currentIdentityUserId">The ID of the currently authenticated IdentityUser.</param>
        /// <returns>An OnboardingCompleteResponse indicating success and the primary PersonId.</returns>
        public async Task<OnboardingCompleteResponse> CompleteOnboardingAsync(OnboardingCompleteRequest request, string currentIdentityUserId)
        {
            if (request == null || string.IsNullOrWhiteSpace(currentIdentityUserId))
            {
                Console.WriteLine("CompleteOnboardingAsync: Invalid request or missing IdentityUser ID.");
                return new OnboardingCompleteResponse { Success = false, Message = "Invalid onboarding request." };
            }

            if (request.PersonDetails == null || string.IsNullOrWhiteSpace(request.PersonDetails.Name))
            {
                Console.WriteLine("CompleteOnboardingAsync: Primary person details (Name) are required.");
                return new OnboardingCompleteResponse { Success = false, Message = "Your name is required to complete onboarding." };
            }

            using var transaction = await _dbContext.Database.BeginTransactionAsync();
            try
            {
                // Retrieve the PersonId for the 'System' user for auditing purposes
                // This is needed to populate CreatedByPersonId for entities created *by* the system (e.g., initial plan)
                var systemPerson = await _dbContext.Persons
                                                 .Where(p => p.Name == "System")
                                                 .FirstOrDefaultAsync();

                if (systemPerson == null)
                {
                    // Fallback: If System person doesn't exist, create it.
                    Console.WriteLine("PersonOrchestrationService: 'System' person not found. Creating a temporary one for auditing.");
                    systemPerson = new PersonEntity
                    {
                        Name = "System",
                        UserId = null,
                        InvitationCode = null
                        // Audit fields will be set by ApplicationDbContext
                    };
                    _dbContext.Persons.Add(systemPerson);
                    await _dbContext.SaveChangesAsync();
                }
                var systemPersonId = systemPerson.Id;


                // 1. Find or Create Primary Person Entity
                PersonEntity primaryPerson = await _dbContext.Persons
                                            .FirstOrDefaultAsync(p => p.UserId == currentIdentityUserId);

                if (primaryPerson == null)
                {
                    // This is the first time onboarding for this IdentityUser, create new PersonEntity
                    primaryPerson = new PersonEntity
                    {
                        Name = request.PersonDetails.Name,
                        UserId = currentIdentityUserId,
                        InvitationCode = await GenerateUniqueInvitationCodeAsync()
                        // Audit fields will be set by ApplicationDbContext
                    };
                    _dbContext.Persons.Add(primaryPerson);
                }
                else
                {
                    // Existing user, update their details
                    primaryPerson.Name = request.PersonDetails.Name;
                    // Audit fields will be set by ApplicationDbContext
                    _dbContext.Persons.Update(primaryPerson);
                }
                await _dbContext.SaveChangesAsync(); // Save to get the Id for new primaryPerson

                // Mapping for client-side temporary IDs to actual database IDs
                // Key: client-side temp ID (e.g., -1, -2, -3)
                // Value: actual DB-generated ID
                var clientSideIdToRealIdMap = new Dictionary<long, long>();
                clientSideIdToRealIdMap.Add(request.PersonId, primaryPerson.Id); // Map frontend's primary person temp ID to real ID

                var allParticipants = new List<PersonEntity> { primaryPerson }; // Start with primary person

                // 2. Process Additional Participants (FR-1.8)
                if (request.HasAdditionalParticipants && request.AdditionalParticipantDetails != null && request.AdditionalParticipantDetails.Any())
                {
                    foreach (var participantDetails in request.AdditionalParticipantDetails)
                    {
                        // Create a new PersonEntity for each additional participant
                        var newParticipant = new PersonEntity
                        {
                            Name = participantDetails.Name,
                            InvitationCode = await GenerateUniqueInvitationCodeAsync()
                            // Audit fields will be set by ApplicationDbContext
                        };
                        _dbContext.Persons.Add(newParticipant);
                        allParticipants.Add(newParticipant); // Add to the list to retrieve generated IDs
                        clientSideIdToRealIdMap.Add(participantDetails.Id, 0); // Placeholder, update after SaveChanges
                    }
                    await _dbContext.SaveChangesAsync(); // Save to get IDs for new additional participants

                    // Update the map with real IDs for additional participants
                    int additionalParticipantCount = 0;
                    foreach (var participantDetails in request.AdditionalParticipantDetails)
                    {
                        // The order of entities in allParticipants (after primaryPerson) will match the order
                        // in which they were added from request.AdditionalParticipantDetails.
                        clientSideIdToRealIdMap[participantDetails.Id] = allParticipants[1 + additionalParticipantCount].Id;
                        additionalParticipantCount++;
                    }
                }

                // 3. Process Person Attributes for Primary Person
                if (request.Attributes != null && request.Attributes.Any())
                {
                    // Fetch existing attributes for the primary person
                    var existingAttrs = await _dbContext.PersonAttributes
                        .Where(pa => pa.PersonId == primaryPerson.Id)
                        .ToListAsync();

                    foreach (var attrRequest in request.Attributes)
                    {
                        var existingAttr = existingAttrs.FirstOrDefault(pa => pa.AttributeTypeId == attrRequest.AttributeTypeRefId);

                        if (existingAttr == null)
                        {
                            _dbContext.PersonAttributes.Add(new PersonAttributeEntity
                            {
                                PersonId = primaryPerson.Id,
                                AttributeTypeId = attrRequest.AttributeTypeRefId,
                                Value = attrRequest.Value
                                // Audit fields will be set by ApplicationDbContext
                            });
                        }
                        else
                        {
                            existingAttr.Value = attrRequest.Value;
                            // Audit fields will be set by ApplicationDbContext
                            _dbContext.PersonAttributes.Update(existingAttr);
                        }
                    }
                }

                // 4. Process Restrictions for all involved Persons/Plan (FR-1.4, FR-2.1 to FR-2.5)
                if (request.Restrictions != null && request.Restrictions.Any())
                {
                    foreach (var restrictionRequest in request.Restrictions)
                    {
                        // Resolve RestrictionTypeRefId (should be from reference data)
                        var restrictionTypeRef = await _dbContext.GroupedReferenceViews
                            .FirstOrDefaultAsync(r => r.ReferenceName == restrictionRequest.Name && r.GroupId == (long)ReferenceDiscriminatorEnum.RestrictionType);

                        if (restrictionTypeRef == null)
                        {
                            Console.WriteLine($"Warning: Restriction type '{restrictionRequest.Name}' not found in Reference data. Skipping.");
                            continue; // Skip if type not found
                        }

                        if (restrictionRequest.AppliesToEntirePlan)
                        {
                            // Find or create the primary plan for the user
                            var primaryPlan = await _dbContext.Plans.FirstOrDefaultAsync(p => p.CreatedByPersonId == primaryPerson.Id);
                            if (primaryPlan == null)
                            {
                                primaryPlan = new PlanEntity
                                {
                                    Name = $"{primaryPerson.Name}'s Default Plan",
                                    InvitationCode = await GenerateUniqueInvitationCodeAsync()
                                    // Audit fields will be set by ApplicationDbContext
                                };
                                _dbContext.Plans.Add(primaryPlan);
                                await _dbContext.SaveChangesAsync(); // Save to get the PlanId
                            }

                            // Check for existing plan-level restriction to prevent duplicates (Rule-2.4)
                            var existingPlanRestriction = await _dbContext.Restrictions
                                .AnyAsync(r => r.PlanId == primaryPlan.Id && r.RestrictionTypeId == restrictionTypeRef.ReferenceId);

                            if (!existingPlanRestriction)
                            {
                                _dbContext.Restrictions.Add(new RestrictionEntity
                                {
                                    PlanId = primaryPlan.Id, // Link to plan
                                    Name = restrictionRequest.Name,
                                    Description = restrictionRequest.Description,
                                    RestrictionTypeId = restrictionTypeRef.ReferenceId
                                });
                            }
                        }
                        else // Applies to specific individuals (restrictionRequest.AffectedPersonIds)
                        {
                            // If AffectedPersonIds is empty but not appliesToEntirePlan, it means it applies to current primary person
                            var actualAffectedPersonClientIds = restrictionRequest.AffectedPersonIds != null && restrictionRequest.AffectedPersonIds.Any()
                                ? restrictionRequest.AffectedPersonIds.ToList()
                                : new List<long> { request.PersonId }; // Default to primary person's client-side ID

                            foreach (var affectedClientSideId in actualAffectedPersonClientIds)
                            {
                                if (!clientSideIdToRealIdMap.TryGetValue(affectedClientSideId, out long realAffectedPersonId))
                                {
                                    Console.WriteLine($"Warning: Could not map client-side ID {affectedClientSideId} to real Person ID. Skipping restriction.");
                                    continue;
                                }

                                // Verify realAffectedPersonId exists in the `allParticipants` list to ensure it's a valid participant in this context
                                var participantInScope = allParticipants.Any(p => p.Id == realAffectedPersonId);
                                if (!participantInScope)
                                {
                                    Console.WriteLine($"Warning: Real Person ID {realAffectedPersonId} not found in current onboarding participants scope. Skipping restriction.");
                                    continue;
                                }

                                // Check for existing person-level restriction to prevent duplicates (Rule-2.4)
                                var existingPersonRestriction = await _dbContext.Restrictions
                                    .AnyAsync(r => r.PersonId == realAffectedPersonId && r.RestrictionTypeId == restrictionTypeRef.ReferenceId);

                                if (!existingPersonRestriction)
                                {
                                    _dbContext.Restrictions.Add(new RestrictionEntity
                                    {
                                        PersonId = realAffectedPersonId, // Link to specific person
                                        Name = restrictionRequest.Name,
                                        Description = restrictionRequest.Description,
                                        RestrictionTypeId = restrictionTypeRef.ReferenceId
                                    });
                                }
                            }
                        }
                    }
                }

                // 5. Handle Plan Invitation Code (FR-3.1, FR-3.2, FR-3.3)
                if (!string.IsNullOrWhiteSpace(request.PlanInvitationCode))
                {
                    var existingPlan = await _dbContext.Plans
                        .Include(p => p.Participants)
                        .FirstOrDefaultAsync(p => p.InvitationCode == request.PlanInvitationCode);

                    if (existingPlan != null)
                    {
                        var isAlreadyParticipant = existingPlan.Participants.Any(pp => pp.PersonId == primaryPerson.Id);
                        if (!isAlreadyParticipant)
                        {
                            var memberRoleRef = await _dbContext.GroupedReferenceViews
                                .FirstOrDefaultAsync(r => r.ReferenceName == "Plan Member" && r.GroupId == (long)ReferenceDiscriminatorEnum.PlanInvitationRole);

                            if (memberRoleRef == null)
                            {
                                Console.WriteLine("Warning: 'Plan Member' role not found in Reference Data. Skipping PlanParticipant creation for invitation.");
                            }
                            else
                            {
                                _dbContext.PlanParticipants.Add(new PlanParticipantEntity
                                {
                                    PlanId = existingPlan.Id,
                                    PersonId = primaryPerson.Id,
                                    RoleRefId = memberRoleRef.ReferenceId
                                    // Audit fields will be set by ApplicationDbContext
                                });
                            }
                        }
                        else
                        {
                            Console.WriteLine($"Info: Person {primaryPerson.Id} is already a participant of plan {existingPlan.Id}. Skipping re-adding.");
                        }
                    }
                    else
                    {
                        Console.WriteLine($"Warning: Invitation code '{request.PlanInvitationCode}' not found. Skipping plan linking.");
                    }
                }

                await _dbContext.SaveChangesAsync();
                await transaction.CommitAsync();
                Console.WriteLine($"Onboarding completed successfully for primary person ID: {primaryPerson.Id}");

                return new OnboardingCompleteResponse
                {
                    Success = true,
                    Message = "Onboarding completed successfully!",
                    NewPersonId = primaryPerson.Id // Return the real ID
                };
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                Console.WriteLine($"Error completing onboarding for IdentityUser ID {currentIdentityUserId}: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                return new OnboardingCompleteResponse
                {
                    Success = false,
                    Message = $"Onboarding failed: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Retrieves the current PersonId from the authenticated user's claims.
        /// This method might become less relevant if PersonId is always managed by the service.
        /// </summary>
        /// <returns>The PersonId if available, otherwise 0.</returns>
        public long GetCurrentPersonId()
        {
            var personIdClaim = _httpContextAccessor.HttpContext?.User?.Claims?.FirstOrDefault(c => c.Type == "PersonId")?.Value;
            if (long.TryParse(personIdClaim, out long personId))
            {
                return personId;
            }
            // If the PersonId claim is not present (e.g., brand new user), return 0 or throw
            // The CompleteOnboardingAsync is now designed to handle this by using IdentityUser ID first.
            return 0; // Or throw InvalidOperationException("PersonId claim is missing or invalid for this operation.")
        }
    }
}
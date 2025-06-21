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
using Nom.Orch.Models.Person;

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
            var systemPerson = await _dbContext.Persons.FirstOrDefaultAsync(p => p.Id == 1L);
            if (systemPerson == null)
            {
                systemPerson = new PersonEntity
                {
                    Id = 1L,
                    Name = "System",
                    UserId = null,
                    InvitationCode = null
                };
                _dbContext.Persons.Add(systemPerson);
                await _dbContext.SaveChangesAsync();
            }

            // Generate a unique invitation code for the new person
            var invitationCode = await GenerateUniqueInvitationCodeAsync();

            var newPerson = new PersonEntity
            {
                Name = personName,
                UserId = identityUserId,
                InvitationCode = invitationCode
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
                isUnique = !await _dbContext.Persons.AnyAsync(p => p.InvitationCode == code);

            } while (!isUnique);

            return code;
        }

         /// <summary>
        /// Handles the complete onboarding process, including saving person details,
        /// attributes, and inferred restrictions.
        /// </summary>
        /// <param name="request">Consolidated onboarding data from the frontend.</param>
        /// <param name="principalPersonId">The ID of the primary authenticated user.</param>
        /// <returns>True if onboarding is successfully completed, false otherwise.</returns>
        public async Task<bool> CompleteOnboardingAsync(OnboardingCompleteRequest request)
        {
            if (request == null)
            {
                Console.WriteLine("CompleteOnboardingAsync: Request is null.");
                return false;
            }

            if (request.PersonDetails == null)
            {
                Console.WriteLine("CompleteOnboardingAsync: PersonDetails are required.");
                return false;
            }

            request.PersonId = GetCurrentPersonId(); // Ensure we have the current PersonId from claims

            using var transaction = await _dbContext.Database.BeginTransactionAsync();
            try
            {
                // 1. Update/Create Primary Person Details
                var primaryPerson = await _dbContext.Persons.FirstOrDefaultAsync(p => p.Id == request.PersonId);
                if (primaryPerson == null)
                {
                    Console.WriteLine($"Primary person with ID {request.PersonId} not found. This should not happen after registration. Creating new person.");
                    // In a production scenario, this might indicate an issue with registration flow.
                    // For now, let's create a new one, but log the anomaly.
                    primaryPerson = new PersonEntity
                    {
                        Id = request.PersonId, // If IDs are pre-assigned or generated on create
                        Name = request.PersonDetails.Name
                        // Other PersonEntity properties that are not part of PersonDetailsRequest
                        // e.g., Email, InvitationCode will be from Identity/registration
                    };
                    _dbContext.Persons.Add(primaryPerson);
                    await _dbContext.SaveChangesAsync(); // Save to get the Id if it's generated
                }
                else
                {
                    primaryPerson.Name = request.PersonDetails.Name;
                    // Update other properties from PersonDetailsRequest as needed
                    _dbContext.Persons.Update(primaryPerson);
                }

                // 2. Process Person Attributes for Primary Person
                if (request.Attributes != null && request.Attributes.Any())
                {
                    foreach (var attrRequest in request.Attributes)
                    {
                        var existingAttr = await _dbContext.PersonAttributes
                            .FirstOrDefaultAsync(pa => pa.PersonId == primaryPerson.Id && pa.AttributeTypeId == attrRequest.AttributeTypeRefId);

                        if (existingAttr == null)
                        {
                            _dbContext.PersonAttributes.Add(new PersonAttributeEntity
                            {
                                PersonId = primaryPerson.Id,
                                AttributeTypeId = attrRequest.AttributeTypeRefId,
                                Value = attrRequest.Value
                            });
                        }
                        else
                        {
                            existingAttr.Value = attrRequest.Value;
                            _dbContext.PersonAttributes.Update(existingAttr);
                        }
                    }
                }

                // 3. Process Additional Participants (FR-1.8)
                var allParticipants = new List<PersonEntity> { primaryPerson };
                if (request.HasAdditionalParticipants && request.AdditionalParticipantDetails != null && request.AdditionalParticipantDetails.Any())
                {
                    foreach (var participantDetails in request.AdditionalParticipantDetails)
                    {
                        // For simplicity, we are creating new PersonEntities here.
                        // In a real app, you might have a different flow (e.g., inviting existing users).
                        var newParticipant = new PersonEntity
                        {
                            Name = participantDetails.Name
                            // Other properties like Email, InvitationCode would be handled in a more complex invite flow
                        };
                        _dbContext.Persons.Add(newParticipant);
                        allParticipants.Add(newParticipant);
                    }
                    await _dbContext.SaveChangesAsync(); // Save to get IDs for new participants
                }

                // 4. Process Restrictions for all involved Persons/Plan
                // Get the ID of the "System" person for auditing purposes
                var systemPersonId = await _dbContext.Persons
                                                 .Where(p => p.Name == "System")
                                                 .Select(p => p.Id)
                                                 .FirstOrDefaultAsync();

                if (systemPersonId == 0)
                {
                    Console.WriteLine("PersonOrchestrationService: 'System' person not found. Please ensure it's seeded.");
                    // Fallback to principalPersonId or throw, depending on policy
                    systemPersonId = request.PersonId;
                }

                if (request.Restrictions != null && request.Restrictions.Any())
                {
                    foreach (var restrictionRequest in request.Restrictions)
                    {
                        // Look up the RestrictionTypeRefId using the restriction name
                        var restrictionTypeRefId = await _restrictionOrchestrationService.GetRestrictionTypeRefIdByNameAsync(restrictionRequest.Name);
                        if (restrictionTypeRefId == 0)
                        {
                            Console.WriteLine($"Warning: Restriction type '{restrictionRequest.Name}' not found in Reference data. Skipping.");
                            continue;
                        }

                        if (restrictionRequest.AppliesToEntirePlan)
                        {
                            // Apply to the primary plan associated with principalPersonId (if such a plan exists/is created here)
                            // For simplicity, let's assume one plan per person or create one for them if it doesn't exist
                            // This part needs careful design. For now, we'll link it to the primary user's first plan or null.
                            var primaryPlan = await _dbContext.Plans.FirstOrDefaultAsync(p => p.CreatedByPersonId == primaryPerson.Id);
                            if (primaryPlan == null)
                            {
                                // Create a default plan if none exists for the primary user
                                primaryPlan = new PlanEntity
                                {
                                    Name = $"{primaryPerson.Name}'s Default Plan",
                                    CreatedByPersonId = primaryPerson.Id,
                                    InvitationCode = Guid.NewGuid().ToString("N").Substring(0, 8) // Generate a simple code
                                };
                                _dbContext.Plans.Add(primaryPlan);
                                await _dbContext.SaveChangesAsync(); // Save to get the PlanId
                            }

                            var existingPlanRestriction = await _dbContext.Restrictions
                                .AnyAsync(r => r.PlanId == primaryPlan.Id && r.RestrictionTypeId == restrictionTypeRefId);

                            if (!existingPlanRestriction)
                            {
                                _dbContext.Restrictions.Add(new RestrictionEntity
                                {
                                    PlanId = primaryPlan.Id,
                                    Name = restrictionRequest.Name,
                                    Description = restrictionRequest.Description,
                                    RestrictionTypeId = restrictionTypeRefId,
                                    CreatedDate = DateTime.UtcNow,
                                    CreatedByPersonId = systemPersonId
                                });
                            }
                        }
                        else // Applies to specific individuals
                        {
                            if (restrictionRequest.AffectedPersonIds != null && restrictionRequest.AffectedPersonIds.Any())
                            {
                                foreach (var affectedPersonId in restrictionRequest.AffectedPersonIds)
                                {
                                    // Verify affectedPersonId exists and is part of this plan's scope
                                    var personExists = allParticipants.Any(p => p.Id == affectedPersonId);
                                    if (!personExists)
                                    {
                                        Console.WriteLine($"Warning: Affected Person ID {affectedPersonId} not found in the current onboarding scope. Skipping restriction for this person.");
                                        continue;
                                    }

                                    var existingPersonRestriction = await _dbContext.Restrictions
                                        .AnyAsync(r => r.PersonId == affectedPersonId && r.RestrictionTypeId == restrictionTypeRefId);

                                    if (!existingPersonRestriction)
                                    {
                                        _dbContext.Restrictions.Add(new RestrictionEntity
                                        {
                                            PersonId = affectedPersonId,
                                            Name = restrictionRequest.Name,
                                            Description = restrictionRequest.Description,
                                            RestrictionTypeId = restrictionTypeRefId,
                                            CreatedDate = DateTime.UtcNow,
                                            CreatedByPersonId = systemPersonId
                                        });
                                    }
                                }
                            }
                        }
                    }
                }

                // 5. Handle Plan Invitation Code (FR-3.1, FR-3.2, FR-3.3)
                if (!string.IsNullOrWhiteSpace(request.PlanInvitationCode))
                {
                    var existingPlan = await _dbContext.Plans
                        .Include(p => p.Participants) // Include participants to check for existing links
                        .FirstOrDefaultAsync(p => p.InvitationCode == request.PlanInvitationCode);

                    if (existingPlan != null)
                    {
                        var isAlreadyParticipant = existingPlan.Participants.Any(pp => pp.PersonId == primaryPerson.Id);
                        if (!isAlreadyParticipant)
                        {
                            var memberRoleRefId = await _dbContext.References
                                .Where(r => r.Name == "Plan Member" && r.Groups.Any(g => g.Id == (long)ReferenceDiscriminatorEnum.PlanInvitationRole))
                                .Select(r => r.Id)
                                .FirstOrDefaultAsync();

                            if (memberRoleRefId == 0)
                            {
                                Console.WriteLine("Warning: 'Plan Member' role not found in Reference Data. Skipping PlanParticipant creation for invitation.");
                            }
                            else
                            {
                                _dbContext.PlanParticipants.Add(new PlanParticipantEntity
                                {
                                    PlanId = existingPlan.Id,
                                    PersonId = primaryPerson.Id,
                                    RoleRefId = memberRoleRefId,
                                    CreatedDate = DateTime.UtcNow,
                                    CreatedByPersonId = request.PersonId
                                });
                            }
                        }
                        else
                        {
                            Console.WriteLine($"Info: Person {request.PersonId} is already a participant of plan {existingPlan.Id}. Skipping re-adding.");
                        }
                    }
                    else
                    {
                        Console.WriteLine($"Warning: Invitation code '{request.PlanInvitationCode}' not found. Skipping plan linking.");
                    }
                }

                await _dbContext.SaveChangesAsync();
                await transaction.CommitAsync();
                Console.WriteLine($"Onboarding completed successfully for person ID: {request.PersonId}");
                return true;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                Console.WriteLine($"Error completing onboarding for person ID {request.PersonId}: {ex.Message}");
                Console.WriteLine(ex.StackTrace); // Log stack trace for detailed debugging
                return false;
            }
        }

        /// <summary>
        /// Retrieves the current PersonId from the authenticated user's claims.
        /// </summary>
        /// <returns>The PersonId if available, otherwise null.</returns>
        public long GetCurrentPersonId()
        {
            var personIdClaim = _httpContextAccessor.HttpContext?.User?.Claims?.FirstOrDefault(c => c.Type == "PersonId")?.Value;
            if (long.TryParse(personIdClaim, out long personId))
            {
                return personId;
            }
            throw new InvalidOperationException("PersonId claim is missing or invalid.");
        }
    }
}
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
        private readonly IPrivacyOrchestrationService _privacyOrchestrationService;

        public PersonOrchestrationService(
            ApplicationDbContext dbContext,
            IRestrictionOrchestrationService restrictionOrchestrationService,
            IPrivacyOrchestrationService privacyOrchestrationService,
            IHttpContextAccessor httpContextAccessor)
        {
            _dbContext = dbContext;
            _httpContextAccessor = httpContextAccessor;
            _restrictionOrchestrationService = restrictionOrchestrationService;
            _privacyOrchestrationService = privacyOrchestrationService;
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
                    InvitationCode = null
                };
                _dbContext.Persons.Add(systemPerson);
                await _dbContext.SaveChangesAsync();
            }

            var invitationCode = await GenerateUniqueInvitationCodeAsync();

            var newPerson = new PersonEntity
            {
                Name = personName,
                UserId = identityUserId,
                InvitationCode = invitationCode
            };

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

                isUnique = !await _dbContext.Persons.AnyAsync(p => p.InvitationCode == code) &&
                           !await _dbContext.Plans.AnyAsync(p => p.InvitationCode == code);

            } while (!isUnique);

            return code;
        }

        /// <summary>
        /// Handles the complete onboarding process for a user, including creating/updating their
        /// Person details, attributes, restrictions, and initial consents.
        /// </summary>
        /// <param name="request">Consolidated onboarding data from the frontend.</param>
        /// <returns>An OnboardingCompleteResponse indicating success and the primary PersonId.</returns>
        public async Task<OnboardingCompleteResponse> CompleteOnboardingAsync(OnboardingCompleteRequest request)
        {
            if (request?.PersonDetails == null || string.IsNullOrWhiteSpace(request.PersonDetails.Name))
            {
                return new OnboardingCompleteResponse { Success = false, Message = "Your name is required to complete onboarding." };
            }

            var currentIdentityUserId = _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);

            using var transaction = await _dbContext.Database.BeginTransactionAsync();
            try
            {
                var systemPerson = await _dbContext.Persons.FirstOrDefaultAsync(p => p.Name == "System");
                if (systemPerson == null)
                {
                    systemPerson = new PersonEntity { Name = "System" };
                    _dbContext.Persons.Add(systemPerson);
                    await _dbContext.SaveChangesAsync();
                }

                // 1. Find or Create Primary Person Entity
                PersonEntity? primaryPerson = await _dbContext.Persons
                                            .FirstOrDefaultAsync(p => p.UserId == currentIdentityUserId);

                if (primaryPerson == null)
                {
                    primaryPerson = new PersonEntity
                    {
                        Name = request.PersonDetails.Name,
                        UserId = currentIdentityUserId,
                        InvitationCode = await GenerateUniqueInvitationCodeAsync()
                    };
                    _dbContext.Persons.Add(primaryPerson);
                }
                else
                {
                    primaryPerson.Name = request.PersonDetails.Name;
                    _dbContext.Persons.Update(primaryPerson);
                }
                await _dbContext.SaveChangesAsync();

                var clientSideIdToRealIdMap = new Dictionary<long, long> { { request.PersonId, primaryPerson.Id } };
                var allParticipants = new List<PersonEntity> { primaryPerson };

                // 2. Process Additional Participants
                if (request.HasAdditionalParticipants && request.AdditionalParticipantDetails != null && request.AdditionalParticipantDetails.Any())
                {
                    foreach (var participantDetails in request.AdditionalParticipantDetails)
                    {
                        var newParticipant = new PersonEntity
                        {
                            Name = participantDetails.Name,
                            InvitationCode = await GenerateUniqueInvitationCodeAsync()
                        };
                        _dbContext.Persons.Add(newParticipant);
                        allParticipants.Add(newParticipant);
                        clientSideIdToRealIdMap.Add(participantDetails.Id, 0);
                    }
                    await _dbContext.SaveChangesAsync();

                    int i = 0;
                    foreach (var participantDetails in request.AdditionalParticipantDetails)
                    {
                        clientSideIdToRealIdMap[participantDetails.Id] = allParticipants[1 + i].Id;
                        i++;
                    }
                }

                // 3. Process Person Attributes for Primary Person
                if (request.Attributes != null && request.Attributes.Any())
                {
                    var existingAttrs = await _dbContext.PersonAttributes.Where(pa => pa.PersonId == primaryPerson.Id).ToListAsync();
                    foreach (var attrRequest in request.Attributes)
                    {
                        var existingAttr = existingAttrs.FirstOrDefault(pa => pa.AttributeTypeId == attrRequest.AttributeTypeRefId);
                        if (existingAttr == null)
                        {
                            _dbContext.PersonAttributes.Add(new PersonAttributeEntity { PersonId = primaryPerson.Id, AttributeTypeId = attrRequest.AttributeTypeRefId, Value = attrRequest.Value });
                        }
                        else
                        {
                            existingAttr.Value = attrRequest.Value;
                            _dbContext.PersonAttributes.Update(existingAttr);
                        }
                    }
                }

                // 4. Process Restrictions
                if (request.Restrictions != null && request.Restrictions.Any())
                {
                    foreach (var restrictionRequest in request.Restrictions)
                    {
                        var restrictionTypeRef = await _dbContext.References.FirstOrDefaultAsync(r => r.Name == restrictionRequest.Name);
                        if (restrictionTypeRef == null) continue;

                        if (restrictionRequest.AppliesToEntirePlan)
                        {
                            var primaryPlan = await _dbContext.Plans.FirstOrDefaultAsync(p => p.CreatedByPersonId == primaryPerson.Id);
                            if (primaryPlan == null)
                            {
                                primaryPlan = new PlanEntity { Name = $"{primaryPerson.Name}'s Default Plan", InvitationCode = await GenerateUniqueInvitationCodeAsync() };
                                _dbContext.Plans.Add(primaryPlan);
                                await _dbContext.SaveChangesAsync();
                            }
                            if (!await _dbContext.Restrictions.AnyAsync(r => r.PlanId == primaryPlan.Id && r.RestrictionTypeId == restrictionTypeRef.Id))
                            {
                                _dbContext.Restrictions.Add(new RestrictionEntity { PlanId = primaryPlan.Id, Name = restrictionRequest.Name, Description = restrictionRequest.Description, RestrictionTypeId = restrictionTypeRef.Id });
                            }
                        }
                        else
                        {
                            var actualAffectedPersonClientIds = restrictionRequest.AffectedPersonIds?.Any() == true ? restrictionRequest.AffectedPersonIds.ToList() : new List<long> { request.PersonId };
                            foreach (var affectedClientSideId in actualAffectedPersonClientIds)
                            {
                                if (clientSideIdToRealIdMap.TryGetValue(affectedClientSideId, out long realAffectedPersonId) && allParticipants.Any(p => p.Id == realAffectedPersonId))
                                {
                                    if (!await _dbContext.Restrictions.AnyAsync(r => r.PersonId == realAffectedPersonId && r.RestrictionTypeId == restrictionTypeRef.Id))
                                    {
                                        _dbContext.Restrictions.Add(new RestrictionEntity { PersonId = realAffectedPersonId, Name = restrictionRequest.Name, Description = restrictionRequest.Description, RestrictionTypeId = restrictionTypeRef.Id });
                                    }
                                }
                            }
                        }
                    }
                }

                // 5. Handle Plan Invitation Code
                if (!string.IsNullOrWhiteSpace(request.PlanInvitationCode))
                {
                    var existingPlan = await _dbContext.Plans.Include(p => p.Participants).FirstOrDefaultAsync(p => p.InvitationCode == request.PlanInvitationCode);
                    if (existingPlan != null && !existingPlan.Participants.Any(pp => pp.PersonId == primaryPerson.Id))
                    {
                        var memberRoleRef = await _dbContext.References.FirstOrDefaultAsync(r => r.Name == "Plan Member");
                        if (memberRoleRef != null)
                        {
                            _dbContext.PlanParticipants.Add(new PlanParticipantEntity { PlanId = existingPlan.Id, PersonId = primaryPerson.Id, RoleRefId = memberRoleRef.Id });
                        }
                    }
                }

                // 6. Process Initial Consents
                if (request.Consents != null && request.Consents.Any())
                {
                    var consentUpdateRequest = new UpdateConsentRequest { Consents = request.Consents };
                    await _privacyOrchestrationService.UpdateConsentAsync(consentUpdateRequest, primaryPerson.Id);
                }

                await _dbContext.SaveChangesAsync();
                await transaction.CommitAsync();

                return new OnboardingCompleteResponse
                {
                    Success = true,
                    Message = "Onboarding completed successfully!",
                    NewPersonId = primaryPerson.Id
                };
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                Console.WriteLine($"Error completing onboarding: {ex.Message}");
                return new OnboardingCompleteResponse { Success = false, Message = $"Onboarding failed: {ex.Message}" };
            }
        }

        /// <summary>
        /// Retrieves the current PersonId from the authenticated user's claims.
        /// </summary>
        public long GetCurrentPersonId()
        {
            var personIdClaim = _httpContextAccessor.HttpContext?.User?.Claims?.FirstOrDefault(c => c.Type == "PersonId")?.Value;
            if (long.TryParse(personIdClaim, out long personId))
            {
                return personId;
            }
            return 0;
        }
    }
}

// File: Nom.Orch/Models/Person/OnboardingCompleteRequest.cs

using Nom.Orch.Models.Privacy;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Nom.Orch.Models.Person
{
    /// <summary>
    /// Simplified model for completing onboarding.
    /// </summary>
    public class OnboardingCompleteRequest
    {
        public long? PersonId { get; set; }
        public PersonDetailsRequest PersonDetails { get; set; } = new();
        public List<PersonAttributeRequest> Attributes { get; set; } = new();
        public List<RestrictionRequest> Restrictions { get; set; } = new();
        public string? PlanInvitationCode { get; set; }
        public bool HasAdditionalParticipants { get; set; }
        public int NumberOfAdditionalParticipants { get; set; }
        public List<PersonDetailsRequest> AdditionalParticipantDetails { get; set; } = new();
        public bool ApplyIndividualPreferencesToEachPerson { get; set; }

        /// <summary>
        /// The "Just me" onboarding fork: silently auto-create a personal
        /// kitchen ("&lt;FirstName&gt;'s Kitchen") containing only this person.
        /// Ignored when the person already belongs to a household.
        /// </summary>
        public bool CreateSoloHousehold { get; set; }
    }
}

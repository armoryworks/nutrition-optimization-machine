// File: Nom.Orch/Models/Person/OnboardingCompleteRequest.cs

using Nom.Orch.Models.Privacy;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Nom.Orch.Models.Person
{
    /// <summary>
    /// Consolidated model for the entire onboarding submission.
    /// Contains data for the primary person, their attributes, restrictions, and consents.
    /// </summary>
    public class OnboardingCompleteRequest
    {
        public long PersonId { get; set; }

        [Required(ErrorMessage = "Person details are required for onboarding.")]
        public PersonDetailsRequest PersonDetails { get; set; } = new PersonDetailsRequest();

        public List<PersonAttributeRequest>? Attributes { get; set; }

        public List<RestrictionRequest>? Restrictions { get; set; }

        /// <summary>
        /// A list of initial consent preferences collected during onboarding.
        /// </summary>
        public List<ConsentRequest>? Consents { get; set; }

        public string? PlanInvitationCode { get; set; }
        public bool HasAdditionalParticipants { get; set; } = false;
        public int NumberOfAdditionalParticipants { get; set; } = 0;
        public List<PersonDetailsRequest>? AdditionalParticipantDetails { get; set; }
        public bool ApplyIndividualPreferencesToEachPerson { get; set; } = false;
    }
}

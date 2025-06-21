using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Nom.Orch.Models.Person
{
    /// <summary>
    /// Consolidated DTO for the entire onboarding submission.
    /// Contains data for the primary person, their attributes, and their restrictions.
    /// </summary>
    public class OnboardingCompleteRequest
    {
        // This PersonId will typically be derived from the authenticated user's claims
        // on the backend, rather than directly from the frontend payload for security.
        // However, it's included here if the frontend needs to pass it for mapping purposes.
        public long PersonId { get; set; }

        [Required(ErrorMessage = "Person details are required for onboarding.")]
        public PersonDetailsRequest PersonDetails { get; set; } = new PersonDetailsRequest();

        public List<PersonAttributeRequest>? Attributes { get; set; }

        public List<RestrictionRequest>? Restrictions { get; set; }

        public string? PlanInvitationCode { get; set; } // For FR-3.1
        public bool HasAdditionalParticipants { get; set; } = false; // For FR-1.8.1
        public int NumberOfAdditionalParticipants { get; set; } = 0; // For FR-1.8.2
        public List<PersonDetailsRequest>? AdditionalParticipantDetails { get; set; } // For names from FR-1.8.3
        public bool ApplyIndividualPreferencesToEachPerson { get; set; } = false; // For FR-1.8.4
        // Note: Restrictions for additional participants will be part of the main Restrictions list,
        // with their affected PersonIds specified.
    }
}

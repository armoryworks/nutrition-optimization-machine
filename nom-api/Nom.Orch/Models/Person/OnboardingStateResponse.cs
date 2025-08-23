using System.Collections.Generic;

namespace Nom.Orch.Models.Person
{
    /// <summary>
    /// Response model for fetching the current onboarding state
    /// </summary>
    public class OnboardingStateResponse
    {
        public bool HasExistingPerson { get; set; }
        public long? PersonId { get; set; }
        public PersonDetailsRequest PersonDetails { get; set; } = new();
        public List<PersonAttributeRequest> Attributes { get; set; } = new();
        public List<RestrictionRequest> Restrictions { get; set; } = new();
        public string? PlanInvitationCode { get; set; }
        public bool HasAdditionalParticipants { get; set; }
        public int NumberOfAdditionalParticipants { get; set; }
        public List<PersonDetailsRequest> AdditionalParticipantDetails { get; set; } = new();
        public bool ApplyIndividualPreferencesToEachPerson { get; set; }
        public int CurrentStep { get; set; }
        public bool IsComplete { get; set; }
    }
}

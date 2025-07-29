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
        public string UserId { get; set; } = string.Empty;
        public string? PlanInvitationCode { get; set; }
        public List<PersonDetailsRequest>? AdditionalParticipants { get; set; }
    }
}

// Nom.Orch/Models/Person/OnboardingCompleteResponse.cs
namespace Nom.Orch.Models.Person
{
    public class OnboardingCompleteResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public long? NewPersonId { get; set; } // The real PersonId of the primary user, if newly created or updated
        // You might extend this to include IDs of additional participants if the frontend needs them
        // public List<long> AdditionalParticipantIds { get; set; }
    }
}
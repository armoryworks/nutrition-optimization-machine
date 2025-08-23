namespace Nom.Orch.Models.Person
{
    public class ClaimInvitationRequest
    {
        public string InvitationCode { get; set; } = string.Empty;
        // public long InviteePersonId { get; set; } // REMOVED - Will be set from claims
    }
} 
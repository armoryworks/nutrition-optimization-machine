using System;

namespace Nom.Orch.Models.Person
{
    public class CreateInvitationRequest
    {
        public long InviterPersonId { get; set; }
        public string InvitationType { get; set; } = string.Empty;
        public long? PlanId { get; set; }
        public DateTime? ExpirationDate { get; set; }
        public string? Notes { get; set; }
    }
} 
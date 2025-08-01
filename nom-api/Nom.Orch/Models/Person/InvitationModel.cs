using System;

namespace Nom.Orch.Models.Person
{
    public class InvitationModel
    {
        public long Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public long InviterPersonId { get; set; }
        public string InviterName { get; set; } = string.Empty;
        public long? InviteePersonId { get; set; }
        public string? InviteeName { get; set; }
        public DateTime? ExpirationDate { get; set; }
        public bool IsUsed { get; set; }
        public DateTime? UsedAt { get; set; }
        public string? Notes { get; set; }
        public string InvitationType { get; set; } = string.Empty;
        public long? PlanId { get; set; }
        public string? PlanName { get; set; }
        public DateTime CreatedDate { get; set; }
    }
}
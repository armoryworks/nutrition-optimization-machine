using System;
using Nom.Data.Person;

namespace Nom.Data.Plan
{
    /// <summary>
    /// Outbox-style record emitted when a managed_enrollment token is redeemed
    /// (or a managed household's membership changes in a way the external
    /// manager must react to). The external tool polls unprocessed rows,
    /// completes its side (e.g. applies a policy template), and stamps
    /// ProcessedAt. NOM never interprets TemplateRef.
    /// </summary>
    public class EnrollmentEventEntity : BaseEntity
    {
        public long HouseholdId { get; set; }
        public virtual HouseholdEntity? Household { get; set; }

        /// <summary>The person whose action produced the event (redeemer/joiner).</summary>
        public long PersonId { get; set; }
        public virtual PersonEntity? Person { get; set; }

        public long? InviteTokenId { get; set; }
        public virtual HouseholdInviteTokenEntity? InviteToken { get; set; }

        /// <summary>"enrollment_redeemed" | "member_joined_managed" | "enrollment_left".</summary>
        public string EventType { get; set; } = string.Empty;

        public string? ManagedBy { get; set; }

        public string? TemplateRef { get; set; }

        public DateTimeOffset? ProcessedAt { get; set; }
    }
}

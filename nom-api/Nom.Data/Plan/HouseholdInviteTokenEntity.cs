// File: Nom.Data/Plan/HouseholdInviteTokenEntity.cs

using System;
using Nom.Data.Audit;

namespace Nom.Data.Plan
{
    public class HouseholdInviteTokenEntity : BaseExpirationLimitedUseEntity
    {
        public long HouseholdId { get; set; }
        public virtual HouseholdEntity? Household { get; set; }

        public string Token { get; set; } = string.Empty;

        /// <summary>
        /// What redeeming this token does: "household_join" (default — today's
        /// family invite) or "managed_enrollment" (places the household under
        /// an external management tool; see design doc §5).
        /// </summary>
        public string Kind { get; set; } = InviteTokenKinds.HouseholdJoin;

        /// <summary>Manager marker applied on managed_enrollment redemption (e.g. "brigade:456").</summary>
        public string? ManagedBy { get; set; }

        /// <summary>Opaque payload recorded on redemption for the external tool; NOM never interprets it.</summary>
        public string? TemplateRef { get; set; }
    }

    /// <summary>Well-known <see cref="HouseholdInviteTokenEntity.Kind"/> values.</summary>
    public static class InviteTokenKinds
    {
        public const string HouseholdJoin = "household_join";
        public const string ManagedEnrollment = "managed_enrollment";
    }
}
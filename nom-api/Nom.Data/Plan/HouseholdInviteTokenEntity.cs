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
    }
}
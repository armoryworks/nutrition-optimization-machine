// File: Nom.Data/Plan/HouseholdWebhookEntity.cs

using System;
using Nom.Data.Audit;

namespace Nom.Data.Plan
{
    public class HouseholdWebhookEntity : BaseEntity
    {
        public long HouseholdId { get; set; }
        public virtual HouseholdEntity? Household { get; set; }

        public string Name { get; set; } = string.Empty;

        public string Url { get; set; } = string.Empty;

        public string? EventType { get; set; }

        public bool IsActive { get; set; } = true;
    }
}
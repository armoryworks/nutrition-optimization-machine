// File: Nom.Data/Plan/HouseholdEventNotifierEntity.cs

using System;
using Nom.Data.Audit;

namespace Nom.Data.Plan
{
    public class HouseholdEventNotifierEntity : BaseEntity
    {
        public long HouseholdId { get; set; }
        public virtual HouseholdEntity? Household { get; set; }

        public string Name { get; set; } = string.Empty;

        public string? EventType { get; set; }

        public string? NotificationType { get; set; }

        public string? Configuration { get; set; }

        public bool IsActive { get; set; } = true;
    }
}
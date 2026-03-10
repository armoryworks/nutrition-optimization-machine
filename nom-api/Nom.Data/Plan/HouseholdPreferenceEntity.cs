// File: Nom.Data/Plan/HouseholdPreferenceEntity.cs

using System;
using Nom.Data.Audit;

namespace Nom.Data.Plan
{
    public class HouseholdPreferenceEntity : BaseEntity
    {
        public long HouseholdId { get; set; }
        public virtual HouseholdEntity? Household { get; set; }

        public string PreferenceKey { get; set; } = string.Empty;

        public string? PreferenceValue { get; set; }

        public string? DataType { get; set; }
    }
}
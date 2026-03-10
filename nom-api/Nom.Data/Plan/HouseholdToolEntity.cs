// File: Nom.Data/Plan/HouseholdToolEntity.cs

using Nom.Data.Audit;
using Nom.Data.Plan;
using Nom.Data.Reference;

namespace Nom.Data.Plan
{
    public class HouseholdToolEntity : BaseEntity
    {
        public long HouseholdId { get; set; }
        public virtual HouseholdEntity? Household { get; set; }

        public long ToolId { get; set; }
        public virtual ReferenceEntity? Tool { get; set; }
    }
}
// File: Nom.Data/Plan/HouseholdToolEntity.cs

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Nom.Data.Audit;
using Nom.Data.Plan;
using Nom.Data.Reference;

namespace Nom.Data.Plan
{
    [Table("HouseholdTool", Schema = "plan")]
    public class HouseholdToolEntity : BaseEntity
    {
        [Required]
        public long HouseholdId { get; set; }
        [ForeignKey(nameof(HouseholdId))]
        public virtual HouseholdEntity? Household { get; set; }

        [Required]
        public long ToolId { get; set; }
        [ForeignKey(nameof(ToolId))]
        public virtual ReferenceEntity? Tool { get; set; }
    }
} 
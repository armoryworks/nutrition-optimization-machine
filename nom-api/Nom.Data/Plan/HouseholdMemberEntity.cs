using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Nom.Data.Audit;
using Nom.Data.Person;
using Nom.Data.Plan;

namespace Nom.Data.Plan
{
    /// <summary>
    /// Represents a member of a household.
    /// Maps to the 'Plan.household_member' table.
    /// </summary>
    [Table("HouseholdMember", Schema = "plan")]
    public class HouseholdMemberEntity : BaseEntity
    {
        [Required]
        public long HouseholdId { get; set; }
        [ForeignKey(nameof(HouseholdId))]
        public virtual HouseholdEntity Household { get; set; } = default!;

        [Required]
        public long PersonId { get; set; }
        [ForeignKey(nameof(PersonId))]
        public virtual PersonEntity Person { get; set; } = default!;

        [Required]
        [MaxLength(50)]
        public string Role { get; set; } = "Member"; // Member, Admin, etc.

        public DateTime? JoinedDate { get; set; }
        public DateTime? LeftDate { get; set; }
        public bool IsActive { get; set; } = true;

        public bool IsAdmin { get; set; } = false;
        public bool CanManage { get; set; } = false;
        public bool CanInvite { get; set; } = false;
    }
}
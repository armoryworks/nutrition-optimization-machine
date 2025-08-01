// File: Nom.Data/Plan/HouseholdInviteTokenEntity.cs

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Nom.Data.Audit;

namespace Nom.Data.Plan
{
    [Table("HouseholdInviteToken", Schema = "plan")]
    public class HouseholdInviteTokenEntity : BaseExpirationLimitedUseEntity
    {
        [Required]
        public long HouseholdId { get; set; }
        [ForeignKey(nameof(HouseholdId))]
        public virtual HouseholdEntity? Household { get; set; }

        [Required]
        [MaxLength(255)]
        public string Token { get; set; } = string.Empty;
    }
}
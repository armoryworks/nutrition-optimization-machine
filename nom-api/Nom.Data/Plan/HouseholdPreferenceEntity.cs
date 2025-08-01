// File: Nom.Data/Plan/HouseholdPreferenceEntity.cs

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Nom.Data.Audit;

namespace Nom.Data.Plan
{
    [Table("HouseholdPreference", Schema = "plan")]
    public class HouseholdPreferenceEntity : BaseEntity
    {
        [Required]
        public long HouseholdId { get; set; }
        [ForeignKey(nameof(HouseholdId))]
        public virtual HouseholdEntity? Household { get; set; }

        [Required]
        [MaxLength(255)]
        public string PreferenceKey { get; set; } = string.Empty;

        [Column(TypeName = "text")]
        public string? PreferenceValue { get; set; }

        [MaxLength(255)]
        public string? DataType { get; set; }
    }
}
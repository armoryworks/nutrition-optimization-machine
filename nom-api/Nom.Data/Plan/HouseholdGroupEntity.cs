using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Nom.Data.Plan
{
    /// <summary>
    /// Represents a grouping category for households.
    /// Maps to the 'plan.HouseholdGroup' table.
    /// </summary>
    [Table("HouseholdGroup", Schema = "plan")]
    public class HouseholdGroupEntity : BaseEntity
    {
        [Required]
        [MaxLength(255)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(2047)]
        public string? Description { get; set; }

        [MaxLength(255)]
        public string? Slug { get; set; }
    }
}

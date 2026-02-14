using System.ComponentModel.DataAnnotations;

namespace Nom.Orch.Models.Cookbook
{
    public class CookbookCreateModel
    {
        [Required]
        public long HouseholdId { get; set; }

        [Required]
        [MaxLength(255)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(2047)]
        public string? Description { get; set; }

        public bool IsPublic { get; set; }
    }
}

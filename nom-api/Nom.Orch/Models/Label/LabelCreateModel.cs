using System.ComponentModel.DataAnnotations;

namespace Nom.Orch.Models.Label
{
    public class LabelCreateModel
    {
        [Required]
        [MaxLength(255)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(50)]
        public string? Color { get; set; }

        [MaxLength(255)]
        public string? GroupName { get; set; }
    }
}

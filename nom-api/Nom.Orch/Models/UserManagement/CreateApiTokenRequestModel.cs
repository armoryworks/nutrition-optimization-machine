using System.ComponentModel.DataAnnotations;

namespace Nom.Orch.Models.UserManagement
{
    public class CreateApiTokenRequestModel
    {
        [Required]
        [MinLength(3)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(500)]
        public string? Description { get; set; }
    }
} 
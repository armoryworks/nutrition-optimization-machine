using System.ComponentModel.DataAnnotations;

namespace Nom.Orch.Models.UserManagement
{
    public class ForgotPasswordRequestModel
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;
    }
} 
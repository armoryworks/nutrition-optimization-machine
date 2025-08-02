using System.ComponentModel.DataAnnotations;

namespace Nom.Orch.Models.UserManagement
{
    public class RegisterUserRequestModel
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;
        
        [Required]
        [MinLength(3)]
        public string Username { get; set; } = string.Empty;
        
        [Required]
        [MinLength(8)]
        public string Password { get; set; } = string.Empty;
        
        [Required]
        [Compare("Password")]
        public string ConfirmPassword { get; set; } = string.Empty;
        
        public string? FullName { get; set; }
        public string? GroupToken { get; set; }
        public string? HouseholdToken { get; set; }
    }
} 
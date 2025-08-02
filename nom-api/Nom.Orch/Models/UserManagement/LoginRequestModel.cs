using System.ComponentModel.DataAnnotations;

namespace Nom.Orch.Models.UserManagement
{
    public class LoginRequestModel
    {
        [Required]
        public string Username { get; set; } = string.Empty;
        
        [Required]
        public string Password { get; set; } = string.Empty;
        
        public bool RememberMe { get; set; } = false;
    }
} 
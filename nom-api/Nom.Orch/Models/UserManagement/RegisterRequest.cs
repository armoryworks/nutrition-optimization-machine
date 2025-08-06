using System.ComponentModel.DataAnnotations;

namespace Nom.Orch.Models.UserManagement
{
    /// <summary>
    /// Request model for user registration with full name support.
    /// </summary>
    public class RegisterRequest
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        [MinLength(6)]
        public string Password { get; set; } = string.Empty;

        /// <summary>
        /// Optional full name that will be used to create a PersonEntity.
        /// </summary>
        public string? FullName { get; set; }
    }
} 
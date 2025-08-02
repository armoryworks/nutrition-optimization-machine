using System.ComponentModel.DataAnnotations;

namespace Nom.Orch.Models.UserManagement
{
    public class CreateUserRequestModel
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
        
        public string? FullName { get; set; }
        public long? GroupId { get; set; }
        public long? HouseholdId { get; set; }
        
        // User permissions
        public bool CanInvite { get; set; } = false;
        public bool CanManage { get; set; } = false;
        public bool CanManageHousehold { get; set; } = false;
        public bool CanOrganize { get; set; } = false;
        public bool IsAdmin { get; set; } = false;
    }
} 
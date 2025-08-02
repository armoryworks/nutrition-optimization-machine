using System.ComponentModel.DataAnnotations;

namespace Nom.Orch.Models.UserManagement
{
    public class UpdateUserRequestModel
    {
        [EmailAddress]
        public string? Email { get; set; }
        
        [MinLength(3)]
        public string? Username { get; set; }
        
        public string? FullName { get; set; }
        public long? GroupId { get; set; }
        public long? HouseholdId { get; set; }
        
        // User permissions (only admins can update these)
        public bool? CanInvite { get; set; }
        public bool? CanManage { get; set; }
        public bool? CanManageHousehold { get; set; }
        public bool? CanOrganize { get; set; }
        public bool? IsAdmin { get; set; }
    }
} 
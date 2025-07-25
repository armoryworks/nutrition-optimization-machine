using System.ComponentModel.DataAnnotations;

namespace Nom.Orch.Models.UserManagement
{
    public class UpdateUserClaimsRequest
    {
        [Required]
        public required string UserId { get; set; }
        public bool CanManageCuration { get; set; }
        public bool CanManageUserRoles { get; set; }
    }
}
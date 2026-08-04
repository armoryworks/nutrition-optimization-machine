namespace Nom.Orch.Models.UserManagement
{
    public class UserClaimsResponseModel
    {
        public string UserId { get; set; } = string.Empty;
        public bool CanManageCuration { get; set; }
        public bool CanManageUserRoles { get; set; }
    }
}

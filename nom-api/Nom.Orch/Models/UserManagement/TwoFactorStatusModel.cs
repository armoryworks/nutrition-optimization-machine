namespace Nom.Orch.Models.UserManagement
{
    public class TwoFactorStatusModel
    {
        public bool IsEnabled { get; set; }
        public bool HasAuthenticator { get; set; }
        public int RecoveryCodesLeft { get; set; }
    }
}

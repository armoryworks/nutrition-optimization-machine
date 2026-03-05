namespace Nom.Orch.Models.UserManagement
{
    public class TwoFactorRecoveryCodesModel
    {
        public string[] RecoveryCodes { get; set; } = Array.Empty<string>();
    }
}

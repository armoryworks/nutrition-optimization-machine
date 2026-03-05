namespace Nom.Orch.Models.UserManagement
{
    public class TwoFactorSetupResponseModel
    {
        public string SharedKey { get; set; } = string.Empty;
        public string AuthenticatorUri { get; set; } = string.Empty;
    }
}

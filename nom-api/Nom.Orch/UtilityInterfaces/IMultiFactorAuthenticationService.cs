namespace Nom.Orch.UtilityInterfaces
{
    /// <summary>
    /// Interface for Multi-Factor Authentication service using TOTP
    /// </summary>
    public interface IMultiFactorAuthenticationService
    {
        /// <summary>
        /// Generates a new MFA secret for a user
        /// </summary>
        /// <param name="userId">The user ID</param>
        /// <returns>The generated secret</returns>
        string GenerateMfaSecret(string userId);

        /// <summary>
        /// Generates a QR code URL for authenticator app setup
        /// </summary>
        /// <param name="userId">The user ID</param>
        /// <param name="userEmail">The user's email</param>
        /// <param name="secret">The MFA secret</param>
        /// <param name="issuer">The issuer name (default: NOM)</param>
        /// <returns>The QR code URL</returns>
        string GenerateQrCodeUrl(string userId, string userEmail, string secret, string issuer = "NOM");

        /// <summary>
        /// Validates a TOTP code for a user
        /// </summary>
        /// <param name="secret">The MFA secret</param>
        /// <param name="code">The TOTP code to validate</param>
        /// <returns>True if valid, false otherwise</returns>
        bool ValidateTotpCode(string secret, string code);

        /// <summary>
        /// Generates a TOTP code for the current time
        /// </summary>
        /// <param name="secret">The MFA secret</param>
        /// <returns>The current TOTP code</returns>
        string GenerateCurrentTotpCode(string secret);

        /// <summary>
        /// Checks if MFA is required for a user
        /// </summary>
        /// <param name="userId">The user ID</param>
        /// <returns>True if MFA is required</returns>
        bool IsMfaRequired(string userId);
    }
} 
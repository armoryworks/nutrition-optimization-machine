namespace Nom.Api.Authentication
{
    /// <summary>
    /// Builds the email-confirmation link. A visitor who registers part-way through
    /// an OIDC sign-in carries the authorize request along, so confirming the email
    /// leads back to the client application instead of stranding them in NOM.
    /// </summary>
    public static class ConfirmationLink
    {
        private const string AuthorizePath = "/connect/authorize";

        /// <summary>
        /// Returns <paramref name="returnUrl"/> when it targets this authority's
        /// authorize endpoint, otherwise null.
        /// </summary>
        public static string? AuthorizeReturnUrl(string? returnUrl) =>
            returnUrl is not null
            && (returnUrl == AuthorizePath || returnUrl.StartsWith(AuthorizePath + "?", StringComparison.Ordinal))
                ? returnUrl
                : null;

        public static string Build(string frontendUrl, string userId, string token, string? returnUrl)
        {
            var link = $"{frontendUrl}/confirm-email?userId={Uri.EscapeDataString(userId)}&token={Uri.EscapeDataString(token)}";
            var authorize = AuthorizeReturnUrl(returnUrl);
            return authorize is null ? link : $"{link}&returnUrl={Uri.EscapeDataString(authorize)}";
        }
    }
}

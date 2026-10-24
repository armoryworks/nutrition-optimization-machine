using System.Net;

namespace Nom.Api.Authentication
{
    /// <summary>
    /// Builds the password-reset email. Identity's /forgotPassword hands the sender a
    /// url-safe reset code; the email turns it into a one-click link to the UI's
    /// reset page, which posts the code back to /resetPassword unchanged.
    /// </summary>
    public static class PasswordResetEmail
    {
        public const string Subject = "Reset your NOM password";

        /// <summary>
        /// Returns the UI link that opens the reset page with the email and code filled in.
        /// </summary>
        public static string Link(string frontendUrl, string email, string resetCode)
        {
            var code = WebUtility.HtmlDecode(resetCode);
            return $"{frontendUrl.TrimEnd('/')}/reset-password?email={Uri.EscapeDataString(email)}&code={Uri.EscapeDataString(code)}";
        }

        /// <summary>
        /// Returns the HTML body, which carries the link as a button and as a copyable address.
        /// </summary>
        public static string HtmlBody(string link)
        {
            var href = WebUtility.HtmlEncode(link);
            return $@"
<html>
<body>
<h2>Reset your password</h2>
<p>We received a request to reset the password for your NOM account. Click the button below to choose a new one:</p>
<p><a href=""{href}"" style=""display:inline-block;padding:10px 18px;background:#fbbf24;color:#1f2937;text-decoration:none;font-weight:600;border-radius:4px"">Reset Password</a></p>
<p>If the button does not work, copy this address into your browser:</p>
<p style=""word-break:break-all""><a href=""{href}"">{href}</a></p>
<p>The link works once and expires after a short time. If it has expired, request a new one from the sign-in page.</p>
<p>If you did not request this, you can safely ignore this email. Your password will not change.</p>
</body>
</html>";
        }

        /// <summary>
        /// Returns the plain-text body for mail clients that do not render HTML.
        /// </summary>
        public static string TextBody(string link) =>
            "Reset your password\n\n" +
            "We received a request to reset the password for your NOM account. Open this link to choose a new one:\n\n" +
            $"{link}\n\n" +
            "The link works once and expires after a short time. If it has expired, request a new one from the sign-in page.\n\n" +
            "If you did not request this, you can safely ignore this email. Your password will not change.\n";
    }
}

namespace Nom.Api.Settings
{
    /// <summary>
    /// SMTP email configuration, bound from the "Email" configuration section.
    /// </summary>
    public class EmailSettings
    {
        public string SmtpHost { get; set; } = "localhost";
        public int SmtpPort { get; set; } = 587;
        public string SmtpUser { get; set; } = string.Empty;
        public string SmtpPassword { get; set; } = string.Empty;
        public string FromAddress { get; set; } = "noreply@nom.local";
        public string FromName { get; set; } = "NOM";
        public bool UseSsl { get; set; } = true;

        /// <summary>
        /// When set, EVERY outbound email is delivered to this address instead of its real
        /// recipient, with the intended recipient recorded in the subject and body.
        ///
        /// This exists for non-production instances. Staging is routinely loaded from a
        /// production snapshot containing real user addresses, so an ordinary SMTP setup there
        /// would email real people from a test system. Leave empty in production — a value here
        /// silently prevents users from receiving their confirmation and reset mail.
        /// </summary>
        public string OverrideRecipient { get; set; } = string.Empty;
    }
}

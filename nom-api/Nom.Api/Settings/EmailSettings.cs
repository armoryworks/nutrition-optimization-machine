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
    }
}

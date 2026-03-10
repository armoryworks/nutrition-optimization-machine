namespace Nom.Api.Settings
{
    /// <summary>
    /// Security headers middleware configuration, bound from the "SecurityHeaders" configuration section.
    /// </summary>
    public class SecurityHeadersSettings
    {
        public bool EnableHsts { get; set; } = true;
    }
}

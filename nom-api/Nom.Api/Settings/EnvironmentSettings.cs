namespace Nom.Api.Settings
{
    /// <summary>
    /// Runtime environment configuration, bound from the "AppEnvironment" configuration section.
    /// </summary>
    public class EnvironmentSettings
    {
        public string AspNetCoreEnvironment { get; set; } = string.Empty;
    }
}

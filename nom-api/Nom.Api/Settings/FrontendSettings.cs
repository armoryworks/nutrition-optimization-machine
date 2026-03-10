namespace Nom.Api.Settings
{
    /// <summary>
    /// Frontend URL configuration, bound from the "Frontend" configuration section.
    /// </summary>
    public class FrontendSettings
    {
        public string Url { get; set; } = "http://localhost:4200";
    }
}

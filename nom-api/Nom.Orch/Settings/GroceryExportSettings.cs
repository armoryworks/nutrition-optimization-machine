namespace Nom.Orch.Settings
{
    /// <summary>
    /// Connection settings for the external grocery-export service. NOM ships
    /// without any retailer integration; operators run their own service
    /// implementing the contract in docs/grocery-integration.md and point these
    /// settings at it. When BaseUrl is empty, the shopping-list export feature
    /// reports no providers and the UI hides it entirely.
    /// </summary>
    public class GroceryExportSettings
    {
        public const string SectionName = "GroceryExport";

        public string? BaseUrl { get; set; }

        /// <summary>Sent as X-Api-Key on every request to the grocery service.</summary>
        public string? ApiKey { get; set; }

        /// <summary>
        /// Sent as X-Instance-Id. Services that gate on an instance allowlist
        /// use this to serve only known deployments.
        /// </summary>
        public string? InstanceId { get; set; }

        public int TimeoutSeconds { get; set; } = 45;
    }
}

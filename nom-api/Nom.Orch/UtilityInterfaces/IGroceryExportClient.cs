using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Nom.Orch.UtilityInterfaces
{
    /// <summary>
    /// Client for the external grocery-export service (contract:
    /// docs/grocery-integration.md). Operator-provided and optional — check
    /// <see cref="IsConfigured"/> before offering the feature.
    /// </summary>
    public interface IGroceryExportClient
    {
        bool IsConfigured { get; }

        Task<List<GroceryProviderInfo>> GetProvidersAsync(CancellationToken cancellationToken = default);

        Task<GroceryExportResult> ExportAsync(GroceryExportRequest request, CancellationToken cancellationToken = default);

        Task<string?> GetAuthorizeUrlAsync(string provider, string redirectUri, string state, CancellationToken cancellationToken = default);

        Task<GroceryTokens?> ExchangeAsync(string provider, string? code, string? redirectUri, string? refreshToken, CancellationToken cancellationToken = default);

        Task<List<GroceryStore>> FindStoresAsync(string provider, string postalCode, CancellationToken cancellationToken = default);
    }

    // DTOs mirroring the grocery service's contract.

    public class GroceryProviderInfo
    {
        public string Key { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>"Text", "Link", or "Cart" — tells the UI what to do with a result.</summary>
        public string Kind { get; set; } = "Text";

        public bool RequiresConnection { get; set; }
        public bool Configured { get; set; }
        public string Description { get; set; } = string.Empty;

        /// <summary>Filled in by NOM: whether THIS user has connected the provider.</summary>
        public bool Connected { get; set; }
    }

    public class GroceryExportItem
    {
        public string Name { get; set; } = string.Empty;
        public decimal? Quantity { get; set; }
        public string? Unit { get; set; }
        public string? PackageHint { get; set; }
        public string? Category { get; set; }
        public string? Note { get; set; }
        public string? Upc { get; set; }
    }

    public class GroceryConnectionDto
    {
        public string? AccessToken { get; set; }
        public string? RefreshToken { get; set; }
        public string? LocationId { get; set; }
    }

    public class GroceryExportRequest
    {
        public string Provider { get; set; } = string.Empty;
        public List<GroceryExportItem> Items { get; set; } = new();
        public string? Title { get; set; }
        public string? Format { get; set; }
        public GroceryConnectionDto? Connection { get; set; }
    }

    public class GroceryUnmatchedItem
    {
        public string Name { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
    }

    public class GroceryExportResult
    {
        public bool Success { get; set; }
        public string Kind { get; set; } = "Text";
        public string? Url { get; set; }
        public string? Text { get; set; }
        public int? AddedCount { get; set; }
        public List<GroceryUnmatchedItem> Unmatched { get; set; } = new();
        public string? Error { get; set; }
    }

    public class GroceryTokens
    {
        public string AccessToken { get; set; } = string.Empty;
        public string? RefreshToken { get; set; }
        public DateTimeOffset? ExpiresAt { get; set; }
    }

    public class GroceryStore
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Address { get; set; }
    }
}

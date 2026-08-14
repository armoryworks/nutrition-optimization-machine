using System.Collections.Generic;
using System.Threading.Tasks;
using Nom.Orch.Models.Shopping;
using Nom.Orch.UtilityInterfaces;

namespace Nom.Orch.Interfaces
{
    /// <summary>
    /// Sends a NOM shopping list to an external destination (share sheet,
    /// Instacart, a retailer cart) via the operator's grocery service, and owns
    /// the per-user retailer connections that cart providers need.
    /// </summary>
    public interface IGroceryExportOrchestrationService
    {
        /// <summary>
        /// Destinations available to this person: what the service offers,
        /// annotated with whether they've connected each one. Empty when no
        /// grocery service is configured — the UI then hides the feature.
        /// </summary>
        Task<List<GroceryProviderInfo>> GetProvidersAsync(long personId);

        Task<GroceryExportResult> ExportListAsync(long shoppingListId, long personId, GroceryExportOptionsModel options);

        /// <summary>
        /// Exports lines the client supplies — used by the shopping view, which
        /// is a live projection with no persisted list behind it.
        /// </summary>
        Task<GroceryExportResult> ExportItemsAsync(long personId, GroceryExportItemsModel model);

        Task<string?> StartConnectionAsync(string provider, long personId, string redirectUri);

        /// <summary>Completes the OAuth handshake; returns false when state/code don't check out.</summary>
        Task<bool> CompleteConnectionAsync(string provider, long personId, string code, string state, string redirectUri);

        Task<List<GroceryStore>> FindStoresAsync(string provider, string postalCode);

        Task<bool> SetStoreAsync(string provider, long personId, string locationId, string? locationName);

        Task<bool> DisconnectAsync(string provider, long personId);
    }
}

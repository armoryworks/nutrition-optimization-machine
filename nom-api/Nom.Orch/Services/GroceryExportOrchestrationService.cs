using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nom.Data;
using Nom.Data.Shopping;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Shopping;
using Nom.Orch.UtilityInterfaces;

namespace Nom.Orch.Services
{
    /// <summary>
    /// Bridges NOM shopping lists to the operator's grocery service: turns list
    /// items into export items (enriched with retail package hints so retailers
    /// match sensible package sizes), and owns per-user retailer connections.
    ///
    /// OAuth tokens are encrypted at rest and never leave this service — the
    /// controller and UI only ever see whether a connection exists.
    /// </summary>
    public class GroceryExportOrchestrationService : IGroceryExportOrchestrationService
    {
        private readonly ApplicationDbContext _db;
        private readonly IGroceryExportClient _client;
        private readonly IRetailPackagingOrchestrationService _packaging;
        private readonly IDataEncryptionService _encryption;
        private readonly ILogger<GroceryExportOrchestrationService> _logger;

        public GroceryExportOrchestrationService(
            ApplicationDbContext db,
            IGroceryExportClient client,
            IRetailPackagingOrchestrationService packaging,
            IDataEncryptionService encryption,
            ILogger<GroceryExportOrchestrationService> logger)
        {
            _db = db;
            _client = client;
            _packaging = packaging;
            _encryption = encryption;
            _logger = logger;
        }

        public async Task<List<GroceryProviderInfo>> GetProvidersAsync(long personId)
        {
            var providers = await _client.GetProvidersAsync();
            if (providers.Count == 0)
            {
                return providers;
            }

            var connected = await _db.GroceryConnections
                .AsNoTracking()
                .Where(c => c.PersonId == personId && !c.IsDeleted)
                .Select(c => c.Provider)
                .ToListAsync();

            foreach (var provider in providers)
            {
                provider.Connected = connected.Contains(provider.Key, StringComparer.OrdinalIgnoreCase);
            }

            return providers;
        }

        public async Task<GroceryExportResult> ExportListAsync(
            long shoppingListId, long personId, GroceryExportOptionsModel options)
        {
            var list = await _db.ShoppingLists
                .Include(l => l.Items.Where(i => !i.IsDeleted))
                    .ThenInclude(i => i.Measurement)
                .Include(l => l.Items.Where(i => !i.IsDeleted))
                    .ThenInclude(i => i.Category)
                .AsNoTracking()
                .FirstOrDefaultAsync(l => l.Id == shoppingListId && !l.IsDeleted);

            if (list == null)
            {
                return new GroceryExportResult { Success = false, Error = "Shopping list not found." };
            }

            var items = list.Items
                .Where(i => !options.ExcludeChecked || !i.IsChecked)
                .OrderBy(i => i.Position ?? int.MaxValue)
                .ToList();

            if (items.Count == 0)
            {
                // "All checked off" only makes sense when there was something
                // to check off — an empty list says so plainly.
                var hasAnyItems = list.Items.Count > 0;
                return new GroceryExportResult
                {
                    Success = false,
                    Error = hasAnyItems && options.ExcludeChecked
                        ? "Everything on this list is already checked off."
                        : "This shopping list is empty.",
                };
            }

            var request = new GroceryExportRequest
            {
                Provider = options.Provider,
                Title = list.Name,
                Format = options.Format,
                Items = await BuildExportItemsAsync(items),
            };

            // Cart providers shop a specific store on the user's behalf.
            var connection = await _db.GroceryConnections
                .FirstOrDefaultAsync(c => c.PersonId == personId &&
                                          c.Provider == options.Provider &&
                                          !c.IsDeleted);
            if (connection != null)
            {
                var tokens = await EnsureFreshTokensAsync(connection);
                if (tokens == null)
                {
                    return new GroceryExportResult
                    {
                        Success = false,
                        Error = $"Your {options.Provider} connection expired — reconnect and try again.",
                    };
                }

                request.Connection = new GroceryConnectionDto
                {
                    AccessToken = tokens.AccessToken,
                    RefreshToken = tokens.RefreshToken,
                    LocationId = connection.LocationId,
                };
            }

            return await _client.ExportAsync(request);
        }

        /// <summary>
        /// Maps list items to export items, attaching the retail package hint
        /// ("5 lb bag") that NOM already derives for shopping, since that is
        /// what makes retailer product matching land on the right size.
        /// </summary>
        private async Task<List<GroceryExportItem>> BuildExportItemsAsync(List<ShoppingListItemEntity> items)
        {
            var hints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var names = items.Select(i => i.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                var lookup = await _packaging.LookupPackagingAsync(names, CancellationToken.None);
                foreach (var result in lookup.Results)
                {
                    var size = result.PackageSize > 0
                        ? result.PackageSize.ToString("0.##", CultureInfo.InvariantCulture) + " " + result.PackageSizeUnit
                        : null;
                    var hint = string.Join(" ", new[] { size, result.PackageName }
                        .Where(s => !string.IsNullOrWhiteSpace(s)));

                    if (!string.IsNullOrWhiteSpace(hint))
                    {
                        hints[result.IngredientPattern] = hint.Trim();
                    }
                }
            }
            catch (Exception ex)
            {
                // A missing hint costs match quality, never the export itself.
                _logger.LogWarning(ex, "Retail packaging lookup failed; exporting without package hints");
            }

            return items.Select(item => new GroceryExportItem
            {
                Name = item.Name,
                Quantity = item.Quantity,
                Unit = item.Measurement?.Name,
                Category = item.Category?.Name,
                Note = item.Note,
                PackageHint = hints.TryGetValue(item.Name, out var hint) ? hint : null,
            }).ToList();
        }

        public async Task<string?> StartConnectionAsync(string provider, long personId, string redirectUri)
        {
            // State ties the callback back to this person and is single-use.
            var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
            var state = $"{personId}.{nonce}";

            var url = await _client.GetAuthorizeUrlAsync(provider, redirectUri, state);
            if (url == null)
            {
                return null;
            }

            var pending = await _db.GroceryConnections
                .FirstOrDefaultAsync(c => c.PersonId == personId && c.Provider == provider && !c.IsDeleted);

            if (pending == null)
            {
                pending = new GroceryConnectionEntity
                {
                    PersonId = personId,
                    Provider = provider,
                    AccessToken = string.Empty,
                    CreatedDate = DateTime.UtcNow,
                    CreatedByPersonId = personId,
                };
                _db.GroceryConnections.Add(pending);
            }

            pending.PendingState = _encryption.EncryptString(state);
            pending.LastModifiedDate = DateTime.UtcNow;
            pending.LastModifiedByPersonId = personId;
            await _db.SaveChangesAsync();

            return url;
        }

        public async Task<bool> CompleteConnectionAsync(
            string provider, long personId, string code, string state, string redirectUri)
        {
            var connection = await _db.GroceryConnections
                .FirstOrDefaultAsync(c => c.PersonId == personId && c.Provider == provider && !c.IsDeleted);

            if (connection == null)
            {
                _logger.LogWarning("Grocery callback for {Provider} with no pending connection", provider);
                return false;
            }

            // The state must match the one we issued — otherwise this callback
            // didn't come from a flow this user started.
            var expected = SafeDecrypt(connection.PendingState);
            if (string.IsNullOrEmpty(expected) || expected != state)
            {
                _logger.LogWarning("Grocery callback state mismatch for {Provider}", provider);
                return false;
            }

            var tokens = await _client.ExchangeAsync(provider, code, redirectUri, refreshToken: null);
            if (tokens == null || string.IsNullOrWhiteSpace(tokens.AccessToken))
            {
                return false;
            }

            connection.AccessToken = _encryption.EncryptString(tokens.AccessToken);
            connection.RefreshToken = string.IsNullOrWhiteSpace(tokens.RefreshToken)
                ? null
                : _encryption.EncryptString(tokens.RefreshToken!);
            connection.ExpiresAtUtc = tokens.ExpiresAt?.UtcDateTime;
            connection.PendingState = null; // handshake complete; nonce is single-use
            connection.LastModifiedDate = DateTime.UtcNow;
            connection.LastModifiedByPersonId = personId;
            await _db.SaveChangesAsync();

            _logger.LogInformation("Grocery connection established for person {PersonId} with {Provider}", personId, provider);
            return true;
        }

        public Task<List<GroceryStore>> FindStoresAsync(string provider, string postalCode) =>
            _client.FindStoresAsync(provider, postalCode);

        public async Task<bool> SetStoreAsync(string provider, long personId, string locationId, string? locationName)
        {
            var connection = await _db.GroceryConnections
                .FirstOrDefaultAsync(c => c.PersonId == personId && c.Provider == provider && !c.IsDeleted);
            if (connection == null)
            {
                return false;
            }

            connection.LocationId = locationId;
            connection.LocationName = locationName;
            connection.LastModifiedDate = DateTime.UtcNow;
            connection.LastModifiedByPersonId = personId;
            await _db.SaveChangesAsync();
            return true;
        }

        public async Task<bool> DisconnectAsync(string provider, long personId)
        {
            var connection = await _db.GroceryConnections
                .FirstOrDefaultAsync(c => c.PersonId == personId && c.Provider == provider && !c.IsDeleted);
            if (connection == null)
            {
                return false;
            }

            // Hard delete: retailer tokens shouldn't linger in soft-deleted rows.
            _db.GroceryConnections.Remove(connection);
            await _db.SaveChangesAsync();
            return true;
        }

        /// <summary>
        /// Decrypts the stored tokens, refreshing first when they're at/near expiry.
        /// Returns null when the connection can no longer be used.
        /// </summary>
        private async Task<GroceryTokens?> EnsureFreshTokensAsync(GroceryConnectionEntity connection)
        {
            var accessToken = SafeDecrypt(connection.AccessToken);
            var refreshToken = SafeDecrypt(connection.RefreshToken);

            if (string.IsNullOrWhiteSpace(accessToken))
            {
                return null; // never completed the handshake
            }

            var expiring = connection.ExpiresAtUtc.HasValue &&
                           connection.ExpiresAtUtc.Value <= DateTime.UtcNow.AddMinutes(2);

            if (expiring && !string.IsNullOrWhiteSpace(refreshToken))
            {
                var refreshed = await _client.ExchangeAsync(connection.Provider, code: null, redirectUri: null, refreshToken);
                if (refreshed == null || string.IsNullOrWhiteSpace(refreshed.AccessToken))
                {
                    return null;
                }

                connection.AccessToken = _encryption.EncryptString(refreshed.AccessToken);
                if (!string.IsNullOrWhiteSpace(refreshed.RefreshToken))
                {
                    connection.RefreshToken = _encryption.EncryptString(refreshed.RefreshToken!);
                }

                connection.ExpiresAtUtc = refreshed.ExpiresAt?.UtcDateTime;
                await _db.SaveChangesAsync();
                return refreshed;
            }

            if (expiring)
            {
                return null; // expired with nothing to refresh from
            }

            return new GroceryTokens
            {
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                ExpiresAt = connection.ExpiresAtUtc.HasValue
                    ? new DateTimeOffset(connection.ExpiresAtUtc.Value, TimeSpan.Zero)
                    : null,
            };
        }

        private string? SafeDecrypt(string? cipherText)
        {
            if (string.IsNullOrWhiteSpace(cipherText))
            {
                return null;
            }

            try
            {
                return _encryption.DecryptString(cipherText);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not decrypt a stored grocery credential");
                return null;
            }
        }
    }
}

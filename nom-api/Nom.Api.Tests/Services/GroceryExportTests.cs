using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nom.Data;
using Nom.Data.Shopping;
using Nom.Orch.Models.Shopping;
using Nom.Orch.Interfaces;
using Nom.Orch.Services;
using Nom.Orch.UtilityInterfaces;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// Covers the parts of grocery export NOM owns: which items are sent, how
    /// package hints and units are attached, and that retailer tokens are only
    /// ever handed over encrypted-at-rest and decrypted in-service.
    /// </summary>
    public class GroceryExportTests
    {
        #region Fakes

        private sealed class FakeGroceryClient : IGroceryExportClient
        {
            public bool IsConfigured { get; set; } = true;
            public GroceryExportRequest? LastRequest { get; private set; }
            public List<GroceryProviderInfo> Providers { get; set; } = new();
            public GroceryExportResult Result { get; set; } = new() { Success = true, Kind = "Text", Text = "ok" };
            public GroceryTokens? Tokens { get; set; }

            public Task<List<GroceryProviderInfo>> GetProvidersAsync(CancellationToken cancellationToken = default)
                => Task.FromResult(Providers);

            public Task<GroceryExportResult> ExportAsync(GroceryExportRequest request, CancellationToken cancellationToken = default)
            {
                LastRequest = request;
                return Task.FromResult(Result);
            }

            public Task<string?> GetAuthorizeUrlAsync(string provider, string redirectUri, string state, CancellationToken cancellationToken = default)
                => Task.FromResult<string?>($"https://retailer.test/auth?state={state}");

            public Task<GroceryTokens?> ExchangeAsync(string provider, string? code, string? redirectUri, string? refreshToken, CancellationToken cancellationToken = default)
                => Task.FromResult(Tokens);

            public Task<List<GroceryStore>> FindStoresAsync(string provider, string postalCode, CancellationToken cancellationToken = default)
                => Task.FromResult(new List<GroceryStore>());
        }

        /// <summary>Reversible stand-in so tests can assert what was stored is not plaintext.</summary>
        private sealed class FakeEncryption : IDataEncryptionService
        {
            public string EncryptString(string plainText) => "enc:" + plainText;
            public string DecryptString(string cipherText) =>
                cipherText.StartsWith("enc:") ? cipherText[4..] : throw new InvalidOperationException("not ciphertext");

            // Unused by grocery export; the interface covers file/byte paths too.
            public byte[] EncryptBytes(byte[] data) => data;
            public byte[] DecryptBytes(byte[] data) => data;
            public Task<byte[]> EncryptFileAsync(byte[] data) => Task.FromResult(data);
            public Task<byte[]> DecryptFileAsync(byte[] data) => Task.FromResult(data);
            public static string GenerateEncryptionKey() => "key";
            public static string GenerateInitializationVector() => "iv";
            public bool IsEncrypted(string value) => value.StartsWith("enc:");
        }

        private sealed class FakePackaging : IRetailPackagingOrchestrationService
        {
            public RetailPackagingLookupResponse Response { get; set; } = new();
            public bool Throw { get; set; }

            public Task<RetailPackagingLookupResponse> LookupPackagingAsync(List<string> ingredientNames, CancellationToken ct)
                => Throw ? throw new InvalidOperationException("lookup down") : Task.FromResult(Response);

            public Task<List<RetailPackagingResponseModel>> GetAllAsync() => Task.FromResult(new List<RetailPackagingResponseModel>());
            public Task<RetailPackagingResponseModel?> GetByIdAsync(long id) => Task.FromResult<RetailPackagingResponseModel?>(null);
            public Task<RetailPackagingResponseModel> CreateAsync(RetailPackagingCreateModel model) => throw new NotImplementedException();
            public Task<RetailPackagingResponseModel?> UpdateAsync(long id, RetailPackagingUpdateModel model) => throw new NotImplementedException();
            public Task<bool> DeleteAsync(long id) => Task.FromResult(false);
        }

        #endregion

        private static ApplicationDbContext NewDb()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"grocery-{Guid.NewGuid()}")
                .Options;
            return new ApplicationDbContext(options);
        }

        private static GroceryExportOrchestrationService Build(
            ApplicationDbContext db, FakeGroceryClient client, FakePackaging? packaging = null) =>
            new(db, client, packaging ?? new FakePackaging(), new FakeEncryption(),
                NullLogger<GroceryExportOrchestrationService>.Instance);

        private static async Task<long> SeedListAsync(ApplicationDbContext db)
        {
            var list = new ShoppingListEntity { Name = "Week of Aug 17", AuthorId = 1, CreatedDate = DateTime.UtcNow };
            db.ShoppingLists.Add(list);
            await db.SaveChangesAsync();

            db.ShoppingListItems.AddRange(
                new ShoppingListItemEntity { ShoppingListId = list.Id, Name = "all-purpose flour", Quantity = 5, Position = 1, CreatedDate = DateTime.UtcNow },
                new ShoppingListItemEntity { ShoppingListId = list.Id, Name = "buttermilk", Quantity = 2, Position = 2, CreatedDate = DateTime.UtcNow },
                new ShoppingListItemEntity { ShoppingListId = list.Id, Name = "already bought", Quantity = 1, IsChecked = true, Position = 3, CreatedDate = DateTime.UtcNow });
            await db.SaveChangesAsync();
            return list.Id;
        }

        [Fact]
        public async Task Export_sends_unchecked_items_in_position_order_with_the_list_title()
        {
            using var db = NewDb();
            var listId = await SeedListAsync(db);
            var client = new FakeGroceryClient();

            var result = await Build(db, client).ExportListAsync(listId, personId: 1,
                new GroceryExportOptionsModel { Provider = "text" });

            Assert.True(result.Success);
            var sent = client.LastRequest!;
            Assert.Equal("Week of Aug 17", sent.Title);
            Assert.Equal(new[] { "all-purpose flour", "buttermilk" }, sent.Items.Select(i => i.Name));
        }

        [Fact]
        public async Task Checked_items_can_be_included_on_request()
        {
            using var db = NewDb();
            var listId = await SeedListAsync(db);
            var client = new FakeGroceryClient();

            await Build(db, client).ExportListAsync(listId, 1,
                new GroceryExportOptionsModel { Provider = "text", ExcludeChecked = false });

            Assert.Equal(3, client.LastRequest!.Items.Count);
        }

        [Fact]
        public async Task Retail_package_hints_are_attached_for_matching()
        {
            using var db = NewDb();
            var listId = await SeedListAsync(db);
            var packaging = new FakePackaging
            {
                Response = new RetailPackagingLookupResponse
                {
                    Results =
                    {
                        new RetailPackagingResponseModel
                        {
                            IngredientPattern = "all-purpose flour",
                            PackageName = "bag",
                            PackageSize = 5,
                            PackageSizeUnit = "lb",
                        },
                    },
                },
            };
            var client = new FakeGroceryClient();

            await Build(db, client, packaging).ExportListAsync(listId, 1,
                new GroceryExportOptionsModel { Provider = "instacart" });

            var flour = client.LastRequest!.Items.Single(i => i.Name == "all-purpose flour");
            Assert.Equal("5 lb bag", flour.PackageHint);
            // No packaging row for buttermilk — hint stays null rather than guessed
            Assert.Null(client.LastRequest.Items.Single(i => i.Name == "buttermilk").PackageHint);
        }

        [Fact]
        public async Task Packaging_lookup_failure_does_not_sink_the_export()
        {
            using var db = NewDb();
            var listId = await SeedListAsync(db);
            var client = new FakeGroceryClient();

            var result = await Build(db, client, new FakePackaging { Throw = true })
                .ExportListAsync(listId, 1, new GroceryExportOptionsModel { Provider = "text" });

            Assert.True(result.Success);
            Assert.Equal(2, client.LastRequest!.Items.Count);
        }

        [Fact]
        public async Task Empty_and_fully_checked_lists_report_clearly_without_calling_out()
        {
            using var db = NewDb();
            var empty = new ShoppingListEntity { Name = "Empty", AuthorId = 1, CreatedDate = DateTime.UtcNow };
            db.ShoppingLists.Add(empty);
            await db.SaveChangesAsync();

            var client = new FakeGroceryClient();
            var result = await Build(db, client).ExportListAsync(empty.Id, 1,
                new GroceryExportOptionsModel { Provider = "text" });

            Assert.False(result.Success);
            Assert.Null(client.LastRequest);
            Assert.Contains("empty", result.Error, StringComparison.OrdinalIgnoreCase);

            // A list that HAS items, all checked, gets the other message
            var listId = await SeedListAsync(db);
            foreach (var item in db.ShoppingListItems.Where(i => i.ShoppingListId == listId))
            {
                item.IsChecked = true;
            }

            await db.SaveChangesAsync();

            var allChecked = await Build(db, client).ExportListAsync(listId, 1,
                new GroceryExportOptionsModel { Provider = "text" });

            Assert.False(allChecked.Success);
            Assert.Contains("checked off", allChecked.Error, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task Missing_list_is_reported_not_thrown()
        {
            using var db = NewDb();
            var result = await Build(db, new FakeGroceryClient())
                .ExportListAsync(9999, 1, new GroceryExportOptionsModel { Provider = "text" });

            Assert.False(result.Success);
            Assert.Contains("not found", result.Error, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task Providers_are_annotated_with_this_users_connection_state()
        {
            using var db = NewDb();
            db.GroceryConnections.Add(new GroceryConnectionEntity
            {
                PersonId = 7,
                Provider = "kroger",
                AccessToken = "enc:token",
                CreatedDate = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();

            var client = new FakeGroceryClient
            {
                Providers =
                {
                    new GroceryProviderInfo { Key = "text", Kind = "Text" },
                    new GroceryProviderInfo { Key = "kroger", Kind = "Cart", RequiresConnection = true },
                },
            };

            var providers = await Build(db, client).GetProvidersAsync(personId: 7);

            Assert.False(providers.Single(p => p.Key == "text").Connected);
            Assert.True(providers.Single(p => p.Key == "kroger").Connected);
            // A different person sees no connection
            Assert.False((await Build(db, client).GetProvidersAsync(8)).Single(p => p.Key == "kroger").Connected);
        }

        [Fact]
        public async Task Connect_handshake_stores_tokens_encrypted_and_clears_the_state_nonce()
        {
            using var db = NewDb();
            var client = new FakeGroceryClient
            {
                Tokens = new GroceryTokens
                {
                    AccessToken = "user-token",
                    RefreshToken = "refresh-token",
                    ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
                },
            };
            var service = Build(db, client);

            var url = await service.StartConnectionAsync("kroger", personId: 5, "https://nom.test/cb");
            Assert.NotNull(url);

            var pending = await db.GroceryConnections.SingleAsync();
            Assert.NotNull(pending.PendingState);
            var state = pending.PendingState!["enc:".Length..];

            var ok = await service.CompleteConnectionAsync("kroger", 5, "the-code", state, "https://nom.test/cb");

            Assert.True(ok);
            var saved = await db.GroceryConnections.SingleAsync();
            Assert.Equal("enc:user-token", saved.AccessToken);       // ciphertext at rest
            Assert.Equal("enc:refresh-token", saved.RefreshToken);
            Assert.Null(saved.PendingState);                          // nonce is single-use
        }

        [Fact]
        public async Task Callback_with_a_forged_state_is_rejected()
        {
            using var db = NewDb();
            var client = new FakeGroceryClient { Tokens = new GroceryTokens { AccessToken = "user-token" } };
            var service = Build(db, client);
            await service.StartConnectionAsync("kroger", 5, "https://nom.test/cb");

            var ok = await service.CompleteConnectionAsync("kroger", 5, "code", "not-the-state", "https://nom.test/cb");

            Assert.False(ok);
            Assert.Empty(await db.GroceryConnections.Where(c => c.AccessToken != string.Empty).ToListAsync());
        }

        [Fact]
        public async Task Cart_export_passes_the_decrypted_token_and_chosen_store()
        {
            using var db = NewDb();
            var listId = await SeedListAsync(db);
            db.GroceryConnections.Add(new GroceryConnectionEntity
            {
                PersonId = 1,
                Provider = "kroger",
                AccessToken = "enc:live-token",
                LocationId = "70100001",
                ExpiresAtUtc = DateTime.UtcNow.AddHours(1),
                CreatedDate = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();

            var client = new FakeGroceryClient();
            await Build(db, client).ExportListAsync(listId, 1, new GroceryExportOptionsModel { Provider = "kroger" });

            Assert.Equal("live-token", client.LastRequest!.Connection!.AccessToken);
            Assert.Equal("70100001", client.LastRequest.Connection.LocationId);
        }

        [Fact]
        public async Task Expired_connection_with_no_refresh_token_asks_the_user_to_reconnect()
        {
            using var db = NewDb();
            var listId = await SeedListAsync(db);
            db.GroceryConnections.Add(new GroceryConnectionEntity
            {
                PersonId = 1,
                Provider = "kroger",
                AccessToken = "enc:stale",
                ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-5),
                CreatedDate = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();

            var client = new FakeGroceryClient();
            var result = await Build(db, client).ExportListAsync(listId, 1,
                new GroceryExportOptionsModel { Provider = "kroger" });

            Assert.False(result.Success);
            Assert.Contains("reconnect", result.Error, StringComparison.OrdinalIgnoreCase);
            Assert.Null(client.LastRequest);
        }

        [Fact]
        public async Task Expiring_connection_is_refreshed_and_the_new_token_persisted()
        {
            using var db = NewDb();
            var listId = await SeedListAsync(db);
            db.GroceryConnections.Add(new GroceryConnectionEntity
            {
                PersonId = 1,
                Provider = "kroger",
                AccessToken = "enc:stale",
                RefreshToken = "enc:refresh",
                LocationId = "70100001",
                ExpiresAtUtc = DateTime.UtcNow.AddSeconds(30),
                CreatedDate = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();

            var client = new FakeGroceryClient
            {
                Tokens = new GroceryTokens
                {
                    AccessToken = "fresh-token",
                    RefreshToken = "new-refresh",
                    ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
                },
            };

            await Build(db, client).ExportListAsync(listId, 1, new GroceryExportOptionsModel { Provider = "kroger" });

            Assert.Equal("fresh-token", client.LastRequest!.Connection!.AccessToken);
            var saved = await db.GroceryConnections.SingleAsync();
            Assert.Equal("enc:fresh-token", saved.AccessToken);
            Assert.Equal("enc:new-refresh", saved.RefreshToken);
        }

        [Fact]
        public async Task Items_export_sends_client_supplied_lines_with_a_default_title()
        {
            using var db = NewDb();
            var client = new FakeGroceryClient();

            var result = await Build(db, client).ExportItemsAsync(personId: 1, new GroceryExportItemsModel
            {
                Provider = "text",
                Items =
                {
                    new GroceryExportLineModel { Name = "  flour  ", Quantity = 5, Unit = "lb", PackageHint = "5 lb bag", Category = "Baking" },
                    new GroceryExportLineModel { Name = "   " },   // blank lines are dropped
                },
            });

            Assert.True(result.Success);
            var sent = client.LastRequest!;
            Assert.Single(sent.Items);
            Assert.Equal("flour", sent.Items[0].Name);              // trimmed
            Assert.Equal("5 lb bag", sent.Items[0].PackageHint);    // hint passes through
            Assert.False(string.IsNullOrWhiteSpace(sent.Title));    // defaulted
        }

        [Fact]
        public async Task Items_export_rejects_an_empty_payload_without_calling_out()
        {
            using var db = NewDb();
            var client = new FakeGroceryClient();

            var result = await Build(db, client).ExportItemsAsync(1, new GroceryExportItemsModel { Provider = "text" });

            Assert.False(result.Success);
            Assert.Null(client.LastRequest);
        }

        [Fact]
        public async Task Items_export_uses_the_callers_retailer_connection()
        {
            using var db = NewDb();
            db.GroceryConnections.Add(new GroceryConnectionEntity
            {
                PersonId = 4,
                Provider = "kroger",
                AccessToken = "enc:live-token",
                LocationId = "70100001",
                ExpiresAtUtc = DateTime.UtcNow.AddHours(1),
                CreatedDate = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();

            var client = new FakeGroceryClient();
            await Build(db, client).ExportItemsAsync(4, new GroceryExportItemsModel
            {
                Provider = "kroger",
                Items = { new GroceryExportLineModel { Name = "milk" } },
            });

            Assert.Equal("live-token", client.LastRequest!.Connection!.AccessToken);
            Assert.Equal("70100001", client.LastRequest.Connection.LocationId);
        }

        [Fact]
        public async Task Disconnect_removes_the_row_rather_than_soft_deleting_the_tokens()
        {
            using var db = NewDb();
            db.GroceryConnections.Add(new GroceryConnectionEntity
            {
                PersonId = 3,
                Provider = "kroger",
                AccessToken = "enc:token",
                CreatedDate = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();

            var service = Build(db, new FakeGroceryClient());
            Assert.True(await service.DisconnectAsync("kroger", 3));

            Assert.Empty(await db.GroceryConnections.IgnoreQueryFilters().ToListAsync());
            Assert.False(await service.DisconnectAsync("kroger", 3));
        }
    }
}

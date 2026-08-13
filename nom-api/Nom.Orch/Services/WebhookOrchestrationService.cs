using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nom.Data;
using Nom.Data.Plan;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Webhook;
using Nom.Orch.UtilityServices;

namespace Nom.Orch.Services
{
    public class WebhookOrchestrationService : IWebhookOrchestrationService
    {
        private readonly ApplicationDbContext _db;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<WebhookOrchestrationService> _logger;

        public WebhookOrchestrationService(
            ApplicationDbContext db,
            IHttpClientFactory httpClientFactory,
            ILogger<WebhookOrchestrationService> logger)
        {
            _db = db;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public async Task<List<WebhookResponseModel>> GetWebhooksAsync(long householdId)
        {
            return await _db.HouseholdWebhooks
                .Where(w => w.HouseholdId == householdId)
                .AsNoTracking()
                .Select(w => MapWebhook(w))
                .ToListAsync();
        }

        public async Task<WebhookResponseModel?> GetWebhookAsync(long id)
        {
            var entity = await _db.HouseholdWebhooks.AsNoTracking().FirstOrDefaultAsync(w => w.Id == id);
            return entity == null ? null : MapWebhook(entity);
        }

        public async Task<long> CreateWebhookAsync(WebhookCreateModel model)
        {
            if (!SsrfGuard.TryValidateUrl(model.Url, out var error))
                throw new ArgumentException(error);

            var entity = new HouseholdWebhookEntity
            {
                HouseholdId = model.HouseholdId,
                Name = model.Name,
                Url = model.Url,
                EventType = model.EventType,
                IsActive = true
            };

            _db.HouseholdWebhooks.Add(entity);
            await _db.SaveChangesAsync();

            _logger.LogInformation("Created webhook {WebhookId} for household {HouseholdId}", entity.Id, model.HouseholdId);
            return entity.Id;
        }

        public async Task<WebhookResponseModel?> UpdateWebhookAsync(long id, WebhookUpdateModel model)
        {
            var entity = await _db.HouseholdWebhooks.FindAsync(id);
            if (entity == null) return null;

            if (model.Url != null && !SsrfGuard.TryValidateUrl(model.Url, out var error))
                throw new ArgumentException(error);

            if (model.Name != null) entity.Name = model.Name;
            if (model.Url != null) entity.Url = model.Url;
            if (model.EventType != null) entity.EventType = model.EventType;
            if (model.IsActive.HasValue) entity.IsActive = model.IsActive.Value;

            await _db.SaveChangesAsync();
            return MapWebhook(entity);
        }

        public async Task<bool> DeleteWebhookAsync(long id)
        {
            var entity = await _db.HouseholdWebhooks.FindAsync(id);
            if (entity == null) return false;

            _db.HouseholdWebhooks.Remove(entity);
            await _db.SaveChangesAsync();
            return true;
        }

        public async Task<bool> TestWebhookAsync(long id)
        {
            var entity = await _db.HouseholdWebhooks.FindAsync(id);
            if (entity == null) return false;

            // Re-validate structurally, then send through the SSRF-guarded client
            // (which re-checks the resolved IP at connect time — defeats rebinding).
            if (!SsrfGuard.TryValidateUrl(entity.Url, out _))
                return false;

            try
            {
                var client = _httpClientFactory.CreateClient("webhook");
                client.Timeout = TimeSpan.FromSeconds(10);
                var payload = new { @event = "test", webhookId = entity.Id, timestamp = DateTime.UtcNow };
                var response = await client.PostAsJsonAsync(entity.Url, payload);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Webhook test failed for {WebhookId}", id);
                return false;
            }
        }

        private static WebhookResponseModel MapWebhook(HouseholdWebhookEntity entity)
        {
            return new WebhookResponseModel
            {
                Id = entity.Id,
                HouseholdId = entity.HouseholdId,
                Name = entity.Name,
                Url = entity.Url,
                EventType = entity.EventType,
                IsActive = entity.IsActive,
                CreatedDate = entity.CreatedDate
            };
        }
    }
}

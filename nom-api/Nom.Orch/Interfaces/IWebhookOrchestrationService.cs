using Nom.Orch.Models.Webhook;

namespace Nom.Orch.Interfaces
{
    public interface IWebhookOrchestrationService
    {
        Task<List<WebhookResponseModel>> GetWebhooksAsync(long householdId);
        Task<WebhookResponseModel?> GetWebhookAsync(long id);
        Task<long> CreateWebhookAsync(WebhookCreateModel model);
        Task<WebhookResponseModel?> UpdateWebhookAsync(long id, WebhookUpdateModel model);
        Task<bool> DeleteWebhookAsync(long id);
        Task<bool> TestWebhookAsync(long id);
    }
}

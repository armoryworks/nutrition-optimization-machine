namespace Nom.Orch.Models.Webhook
{
    public class WebhookResponseModel
    {
        public long Id { get; set; }
        public long HouseholdId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string? EventType { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedDate { get; set; }
    }
}

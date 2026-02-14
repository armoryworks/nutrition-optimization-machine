using System.ComponentModel.DataAnnotations;

namespace Nom.Orch.Models.Webhook
{
    public class WebhookCreateModel
    {
        [Required]
        public long HouseholdId { get; set; }

        [Required]
        [MaxLength(255)]
        public string Name { get; set; } = string.Empty;

        [Required]
        [MaxLength(2047)]
        public string Url { get; set; } = string.Empty;

        [MaxLength(255)]
        public string? EventType { get; set; }
    }
}

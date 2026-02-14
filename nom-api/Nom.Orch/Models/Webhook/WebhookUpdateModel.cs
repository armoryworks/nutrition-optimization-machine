using System.ComponentModel.DataAnnotations;

namespace Nom.Orch.Models.Webhook
{
    public class WebhookUpdateModel
    {
        [MaxLength(255)]
        public string? Name { get; set; }

        [MaxLength(2047)]
        public string? Url { get; set; }

        [MaxLength(255)]
        public string? EventType { get; set; }

        public bool? IsActive { get; set; }
    }
}

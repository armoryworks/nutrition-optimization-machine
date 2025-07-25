using System.ComponentModel.DataAnnotations;

namespace Nom.Orch.Models.Communication
{
    public class SendMessageRequest
    {
        public long ThreadId { get; set; }
        [Required]
        public required string Content { get; set; }
    }
}
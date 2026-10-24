using System.ComponentModel.DataAnnotations;

namespace Nom.Orch.Models.Communication
{
    public class SendMessageRequest
    {
        public long ThreadId { get; set; }
        [Required, MaxLength(4000)]
        public required string Content { get; set; }
    }
}
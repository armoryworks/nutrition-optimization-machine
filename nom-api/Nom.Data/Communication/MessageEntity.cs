using Nom.Data.Person;
using System.ComponentModel.DataAnnotations.Schema;

namespace Nom.Data.Communication
{
    [Table("Messages")]
    public class MessageEntity : BaseEntity
    {
        public long MessageThreadId { get; set; }
        [ForeignKey("MessageThreadId")]
        public virtual MessageThreadEntity? MessageThread { get; set; }

        public long SenderPersonId { get; set; }
        [ForeignKey("SenderPersonId")]
        public virtual PersonEntity? SenderPerson { get; set; }

        public string Content { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public bool IsRead { get; set; } = false;
    }
}
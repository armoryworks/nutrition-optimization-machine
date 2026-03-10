using Nom.Data.Person;

namespace Nom.Data.Communication
{
    public class MessageEntity : BaseEntity
    {
        public long MessageThreadId { get; set; }
        public virtual MessageThreadEntity? MessageThread { get; set; }

        public long SenderPersonId { get; set; }
        public virtual PersonEntity? SenderPerson { get; set; }

        public string Content { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public bool IsRead { get; set; } = false;
    }
}
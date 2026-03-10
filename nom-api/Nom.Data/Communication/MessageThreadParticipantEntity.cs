using Nom.Data.Person;

namespace Nom.Data.Communication
{
    public class MessageThreadParticipantEntity : BaseEntity
    {
        public long MessageThreadId { get; set; }
        public virtual MessageThreadEntity? MessageThread { get; set; }

        public long PersonId { get; set; }
        public virtual PersonEntity? Person { get; set; }

        public DateTime DateJoined { get; set; } = DateTime.UtcNow;
        public bool IsArchived { get; set; } = false;
        public bool IsPinned { get; set; } = false;
    }
}
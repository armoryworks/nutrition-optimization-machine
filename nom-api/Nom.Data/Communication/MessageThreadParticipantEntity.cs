using Nom.Data.Person;
using System.ComponentModel.DataAnnotations.Schema;

namespace Nom.Data.Communication
{
    [Table("MessageThreadParticipants")]
    public class MessageThreadParticipantEntity : BaseEntity
    {
        public long MessageThreadId { get; set; }
        [ForeignKey("MessageThreadId")]
        public virtual MessageThreadEntity? MessageThread { get; set; }

        public long PersonId { get; set; }
        [ForeignKey("PersonId")]
        public virtual PersonEntity? Person { get; set; }

        public DateTime DateJoined { get; set; } = DateTime.UtcNow;
        public bool IsArchived { get; set; } = false;
        public bool IsPinned { get; set; } = false;
    }
}
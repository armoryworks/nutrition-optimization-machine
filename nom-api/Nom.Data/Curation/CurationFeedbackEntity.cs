// File: Nom.Data/Curation/CurationFeedbackEntity.cs

using System;
using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;
using Nom.Data.Audit;
using Nom.Data.Person;
using Nom.Data.Reference;

namespace Nom.Data.Curation
{
    [Table("CurationFeedback", Schema = "curation")]
    public class CurationFeedbackEntity : BaseEntity
    {
        public long EntityId { get; set; }

        [Required]
        public long EntityTypeId { get; set; }
        [ForeignKey(nameof(EntityTypeId))]
        public virtual ReferenceEntity? EntityType { get; set; }

        [Required]
        public long AdminId { get; set; }
        [ForeignKey(nameof(AdminId))]
        public virtual PersonEntity? Admin { get; set; }

        [Required]
        public string? FeedbackNotes { get; set; }

        [Required]
        public long FeedbackTypeId { get; set; }
        [ForeignKey(nameof(FeedbackTypeId))]
        public virtual ReferenceEntity? FeedbackType { get; set; }

        public DateTime DateCreated { get; set; } = DateTime.UtcNow;
    }
}
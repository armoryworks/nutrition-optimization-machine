// File: Nom.Data/Curation/CurationFeedbackEntity.cs

using System;
using Nom.Data.Audit;
using Nom.Data.Person;
using Nom.Data.Reference;

namespace Nom.Data.Curation
{
    public class CurationFeedbackEntity : BaseEntity
    {
        public long EntityId { get; set; }

        public long EntityTypeId { get; set; }
        public virtual ReferenceEntity? EntityType { get; set; }

        public long AdminId { get; set; }
        public virtual PersonEntity? Admin { get; set; }

        public string? FeedbackNotes { get; set; }

        public long FeedbackTypeId { get; set; }
        public virtual ReferenceEntity? FeedbackType { get; set; }
    }
}
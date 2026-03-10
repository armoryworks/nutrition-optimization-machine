// File: Nom.Data/Recipe/RecipeTimelineEventEntity.cs

using System;
using Nom.Data.Audit;
using Nom.Data.Person;
using Nom.Data.Reference;

namespace Nom.Data.Recipe
{
    public class RecipeTimelineEventEntity : BaseEntity
    {
        public long RecipeId { get; set; }
        public virtual RecipeEntity? Recipe { get; set; }

        public long ActorId { get; set; }
        public virtual PersonEntity? Actor { get; set; }

        public long EventTypeId { get; set; }
        public virtual ReferenceEntity? EventType { get; set; }

        public string Title { get; set; } = string.Empty;

        public string? Description { get; set; }

        public string? Details { get; set; }

        public DateTime? EventDate { get; set; }
    }
}

// File: Nom.Data/Recipe/RecipeTimelineEventEntity.cs

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Nom.Data.Audit;
using Nom.Data.Person;
using Nom.Data.Reference;

namespace Nom.Data.Recipe
{
    [Table("RecipeTimelineEvent", Schema = "recipe")]
    public class RecipeTimelineEventEntity : BaseEntity
    {
        [Required]
        public long RecipeId { get; set; }
        [ForeignKey(nameof(RecipeId))]
        public virtual RecipeEntity? Recipe { get; set; }

        [Required]
        public long ActorId { get; set; }
        [ForeignKey(nameof(ActorId))]
        public virtual PersonEntity? Actor { get; set; }

        [Required]
        public long EventTypeId { get; set; }
        [ForeignKey(nameof(EventTypeId))]
        public virtual ReferenceEntity? EventType { get; set; }

        [Required]
        [MaxLength(255)]
        public string Title { get; set; } = string.Empty;

        [MaxLength(2047)]
        public string? Description { get; set; }

        [Column(TypeName = "text")]
        public string? Details { get; set; }

        public DateTime? EventDate { get; set; }
    }
} 
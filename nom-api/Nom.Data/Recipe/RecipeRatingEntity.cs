// File: Nom.Data/Recipe/RecipeRatingEntity.cs

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Nom.Data.Audit;
using Nom.Data.Person;

namespace Nom.Data.Recipe
{
    [Table("RecipeRating", Schema = "recipe")]
    public class RecipeRatingEntity : BaseEntity
    {
        [Required]
        public long RecipeId { get; set; }
        [ForeignKey(nameof(RecipeId))]
        public virtual RecipeEntity? Recipe { get; set; }

        [Required]
        public long RaterId { get; set; }
        [ForeignKey(nameof(RaterId))]
        public virtual PersonEntity? Rater { get; set; }

        [Required]
        [Column(TypeName = "decimal(3,2)")]
        public decimal Rating { get; set; }

        public DateTime? DateRated { get; set; }
    }
} 
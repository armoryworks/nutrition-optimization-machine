// File: Nom.Data/Recipe/RecipeNoteEntity.cs

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Nom.Data.Audit;
using Nom.Data.Person;

namespace Nom.Data.Recipe
{
    [Table("RecipeNote", Schema = "recipe")]
    public class RecipeNoteEntity : BaseEntity
    {
        [Required]
        public long RecipeId { get; set; }
        [ForeignKey(nameof(RecipeId))]
        public virtual RecipeEntity? Recipe { get; set; }

        [Required]
        public long AuthorId { get; set; }
        [ForeignKey(nameof(AuthorId))]
        public virtual PersonEntity? Author { get; set; }

        [Required]
        [Column(TypeName = "text")]
        public string Note { get; set; } = string.Empty;

        public bool IsPublic { get; set; } = false;

        [Required]

        [MaxLength(255)]
        public string? Title { get; set; }
    }
}
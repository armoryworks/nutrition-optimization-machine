// File: Nom.Data/Recipe/RecipeTagEntity.cs

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Nom.Data.Audit;
using Nom.Data.Reference;

namespace Nom.Data.Recipe
{
    [Table("RecipeTag", Schema = "recipe")]
    public class RecipeTagEntity : BaseEntity
    {
        [Required]
        public long RecipeId { get; set; }
        [ForeignKey(nameof(RecipeId))]
        public virtual RecipeEntity? Recipe { get; set; }

        [Required]
        public long TagId { get; set; }
        [ForeignKey(nameof(TagId))]
        public virtual ReferenceEntity? Tag { get; set; }
    }
} 
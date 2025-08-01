// File: Nom.Data/Recipe/RecipeCategoryEntity.cs

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Nom.Data.Audit;
using Nom.Data.Reference;

namespace Nom.Data.Recipe
{
    [Table("RecipeCategory", Schema = "recipe")]
    public class RecipeCategoryEntity : BaseEntity
    {
        [Required]
        public long RecipeId { get; set; }
        [ForeignKey(nameof(RecipeId))]
        public virtual RecipeEntity? Recipe { get; set; }

        [Required]
        public long CategoryId { get; set; }
        [ForeignKey(nameof(CategoryId))]
        public virtual ReferenceEntity? Category { get; set; }
    }
} 
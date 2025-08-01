// File: Nom.Data/Recipe/RecipeToolEntity.cs

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Nom.Data.Audit;
using Nom.Data.Reference;

namespace Nom.Data.Recipe
{
    [Table("RecipeTool", Schema = "recipe")]
    public class RecipeToolEntity : BaseEntity
    {
        [Required]
        public long RecipeId { get; set; }
        [ForeignKey(nameof(RecipeId))]
        public virtual RecipeEntity? Recipe { get; set; }

        [Required]
        public long ToolId { get; set; }
        [ForeignKey(nameof(ToolId))]
        public virtual ReferenceEntity? Tool { get; set; }
    }
} 
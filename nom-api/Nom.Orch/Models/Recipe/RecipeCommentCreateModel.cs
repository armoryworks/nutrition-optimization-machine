using System.ComponentModel.DataAnnotations;

namespace Nom.Orch.Models.Recipe
{
    public class RecipeCommentCreateModel
    {
        [Required(ErrorMessage = "Recipe ID is required.")]
        public long RecipeId { get; set; }

        [Required(ErrorMessage = "Comment text is required.")]
        [StringLength(2047, ErrorMessage = "Comment text cannot exceed 2047 characters.")]
        public required string Comment { get; set; }
    }
}
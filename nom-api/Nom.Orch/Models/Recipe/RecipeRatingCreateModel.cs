using System.ComponentModel.DataAnnotations;

namespace Nom.Orch.Models.Recipe
{
    public class RecipeRatingCreateModel
    {
        [Required(ErrorMessage = "Recipe ID is required.")]
        public long RecipeId { get; set; }

        [Required(ErrorMessage = "Rating is required.")]
        [Range(1, 5, ErrorMessage = "Rating must be between 1 and 5.")]
        public decimal Rating { get; set; }
    }
} 
using System.ComponentModel.DataAnnotations;

namespace Nom.Orch.Models.Recipe
{
    public class RecipeShareTokenCreateModel
    {
        [Required(ErrorMessage = "Recipe ID is required.")]
        public long RecipeId { get; set; }

        [StringLength(511, ErrorMessage = "Share name cannot exceed 511 characters.")]
        public string? ShareName { get; set; }

        public bool IsPublic { get; set; } = false;

        public int? UsesLeft { get; set; }

        public DateTime? ExpirationDate { get; set; }
    }
}
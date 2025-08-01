using System.ComponentModel.DataAnnotations;

namespace Nom.Orch.Models.Recipe
{
    public class RecipeNoteCreateModel
    {
        [Required(ErrorMessage = "Recipe ID is required.")]
        public long RecipeId { get; set; }

        [StringLength(255, ErrorMessage = "Note title cannot exceed 255 characters.")]
        public string? Title { get; set; }

        [Required(ErrorMessage = "Note text is required.")]
        [StringLength(2047, ErrorMessage = "Note text cannot exceed 2047 characters.")]
        public required string Note { get; set; }

        public bool IsPublic { get; set; } = false;
    }
} 
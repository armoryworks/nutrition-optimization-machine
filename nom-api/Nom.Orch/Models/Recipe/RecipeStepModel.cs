// File: Nom.Orch/Models/Recipe/RecipeStepModel.cs

using System.ComponentModel.DataAnnotations;

namespace Nom.Orch.Models.Recipe
{
    public class RecipeStepModel
    {
        public long Id { get; set; }
        
        [Required]
        public string Description { get; set; } = string.Empty;
        
        public int Order { get; set; }
    }
}
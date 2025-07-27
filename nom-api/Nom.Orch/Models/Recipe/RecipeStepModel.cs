// File: Nom.Orch/Models/Recipe/RecipeStepModel.cs

using System.ComponentModel.DataAnnotations;

namespace Nom.Orch.Models.Recipe
{
    public class RecipeStepModel
    {
        [Required]
        public string Description { get; set; }
    }
}
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace Nom.Orch.Models.Recipe
{
    /// <summary>
    /// Base model for bulk operations
    /// </summary>
    public class RecipeBulkBaseModel
    {
        [Required]
        public List<long> RecipeIds { get; set; } = new();
    }
} 
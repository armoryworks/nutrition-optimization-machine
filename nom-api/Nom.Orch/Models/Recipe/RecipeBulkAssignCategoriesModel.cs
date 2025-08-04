using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace Nom.Orch.Models.Recipe
{
    /// <summary>
    /// Model for assigning categories to recipes
    /// </summary>
    public class RecipeBulkAssignCategoriesModel : RecipeBulkBaseModel
    {
        [Required]
        public List<string> Categories { get; set; } = new();
    }
} 
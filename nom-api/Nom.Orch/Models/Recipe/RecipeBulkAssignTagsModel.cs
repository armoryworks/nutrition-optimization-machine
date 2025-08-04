using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace Nom.Orch.Models.Recipe
{
    /// <summary>
    /// Model for assigning tags to recipes
    /// </summary>
    public class RecipeBulkAssignTagsModel : RecipeBulkBaseModel
    {
        [Required]
        public List<string> Tags { get; set; } = new();
    }
} 
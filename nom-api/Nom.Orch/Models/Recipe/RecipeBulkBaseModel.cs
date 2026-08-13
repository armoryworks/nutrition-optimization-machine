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

        /// <summary>Set server-side from the caller's identity; scopes the operation to recipes they author. Never body-bound.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public long? RequesterPersonId { get; set; }
    }
} 
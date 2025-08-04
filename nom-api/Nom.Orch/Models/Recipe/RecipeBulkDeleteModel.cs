using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace Nom.Orch.Models.Recipe
{
    /// <summary>
    /// Model for deleting recipes
    /// </summary>
    public class RecipeBulkDeleteModel : RecipeBulkBaseModel
    {
        public bool Permanent { get; set; } = false;
    }
} 
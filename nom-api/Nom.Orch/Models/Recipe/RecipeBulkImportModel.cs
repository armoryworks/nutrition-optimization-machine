using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace Nom.Orch.Models.Recipe
{
    /// <summary>
    /// Model for importing recipes from file
    /// </summary>
    public class RecipeBulkImportModel
    {
        [Required]
        public IFormFile File { get; set; } = null!;
        public ExportTypes ImportType { get; set; } = ExportTypes.Json;
        public bool OverwriteExisting { get; set; } = false;
        public List<string>? DefaultCategories { get; set; }
        public List<string>? DefaultTags { get; set; }
    }
} 
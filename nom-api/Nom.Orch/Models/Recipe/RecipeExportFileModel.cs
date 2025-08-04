using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace Nom.Orch.Models.Recipe
{
    /// <summary>
    /// Model for export file information
    /// </summary>
    public class RecipeExportFileModel
    {
        public long ExportId { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public string ContentType { get; set; } = string.Empty;
        public DateTime CreatedDate { get; set; }
        public DateTime ExpiryDate { get; set; }
        public int RecipeCount { get; set; }
        public ExportTypes ExportType { get; set; }
    }
} 
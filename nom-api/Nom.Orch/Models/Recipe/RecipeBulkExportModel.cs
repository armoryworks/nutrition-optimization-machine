using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace Nom.Orch.Models.Recipe
{
    /// <summary>
    /// Model for exporting recipes
    /// </summary>
    public class RecipeBulkExportModel : RecipeBulkBaseModel
    {
        public ExportTypes ExportType { get; set; } = ExportTypes.Json;
        public bool IncludeImages { get; set; } = true;
        public bool IncludeMetadata { get; set; } = true;
    }
} 
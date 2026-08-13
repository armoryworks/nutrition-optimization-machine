using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace Nom.Orch.Models.Recipe
{
    /// <summary>
    /// Model for exporting recipes
    /// </summary>
    public class RecipeBulkExportModel : RecipeBulkBaseModel
    {
        /// <summary>Set by the controller from the authenticated user — never bound from the body.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public long? RequesterPersonId { get; set; }

        public ExportTypes ExportType { get; set; } = ExportTypes.Json;
        public bool IncludeImages { get; set; } = true;
        public bool IncludeMetadata { get; set; } = true;
    }
} 
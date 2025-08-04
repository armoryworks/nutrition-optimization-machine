using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace Nom.Orch.Models.Recipe
{
    /// <summary>
    /// Model for updating recipe settings
    /// </summary>
    public class RecipeBulkUpdateSettingsModel : RecipeBulkBaseModel
    {
        public bool? IsPublic { get; set; }
        public bool? IsArchived { get; set; }
        public string? CurationStatus { get; set; }
        public string? Notes { get; set; }
    }
} 
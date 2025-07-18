// File: Nom.Orch/Models/Privacy/DataExportRequest.cs

using System.ComponentModel.DataAnnotations;

namespace Nom.Orch.Models.Privacy
{
    /// <summary>
    /// Represents a user's request to export their personal data.
    /// </summary>
    public class DataExportRequest
    {
        /// <summary>
        /// The format for the data export (e.g., "json", "csv").
        /// </summary>
        [Required]
        public string Format { get; set; } = "json";
    }
}

using System.ComponentModel.DataAnnotations;

namespace Nom.Orch.Models.Shopping
{
    public class GroceryExportOptionsModel
    {
        [Required]
        public string Provider { get; set; } = string.Empty;

        /// <summary>Text providers only: "plain" (default), "markdown", or "csv".</summary>
        public string? Format { get; set; }

        /// <summary>Leave checked-off items out of the export. Defaults to true.</summary>
        public bool ExcludeChecked { get; set; } = true;
    }

    public class GroceryStoreSelectionModel
    {
        [Required]
        public string LocationId { get; set; } = string.Empty;

        public string? LocationName { get; set; }
    }
}

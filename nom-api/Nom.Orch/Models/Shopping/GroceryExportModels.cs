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

    /// <summary>
    /// Export items supplied by the caller rather than a persisted list. NOM's
    /// shopping view is a live projection over the meal plan, pantry, and retail
    /// packaging — there is often no ShoppingList row behind what the user sees,
    /// so the client sends the lines it is displaying.
    /// </summary>
    public class GroceryExportItemsModel
    {
        [Required]
        public string Provider { get; set; } = string.Empty;

        public string? Format { get; set; }

        /// <summary>Title for the destination list; defaults to a dated name.</summary>
        public string? Title { get; set; }

        [Required]
        public List<GroceryExportLineModel> Items { get; set; } = new();
    }

    public class GroceryExportLineModel
    {
        [Required]
        public string Name { get; set; } = string.Empty;

        public decimal? Quantity { get; set; }
        public string? Unit { get; set; }

        /// <summary>Retail package hint ("5 lb bag"); the client already computes this.</summary>
        public string? PackageHint { get; set; }

        /// <summary>Aisle/department, preserved in the export.</summary>
        public string? Category { get; set; }

        public string? Note { get; set; }
    }

    public class GroceryStoreSelectionModel
    {
        [Required]
        public string LocationId { get; set; } = string.Empty;

        public string? LocationName { get; set; }
    }
}

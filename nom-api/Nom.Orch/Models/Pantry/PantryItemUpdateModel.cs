using System;

namespace Nom.Orch.Models.Pantry
{
    /// <summary>
    /// Request model for updating a pantry item.
    /// </summary>
    public class PantryItemUpdateModel
    {
        public decimal? Quantity { get; set; }
        public long? MeasurementId { get; set; }
        public DateOnly? ExpectedExpirationDate { get; set; }
        public long? ItemStatusTypeId { get; set; }
        public string? SourceLocation { get; set; }
        public string? Notes { get; set; }
    }
}

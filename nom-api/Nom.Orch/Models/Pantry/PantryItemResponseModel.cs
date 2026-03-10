using System;

namespace Nom.Orch.Models.Pantry
{
    /// <summary>
    /// Response model for a pantry item.
    /// </summary>
    public class PantryItemResponseModel
    {
        public long Id { get; set; }
        public long HouseholdId { get; set; }
        public long IngredientId { get; set; }
        public string IngredientName { get; set; } = string.Empty;
        public decimal Quantity { get; set; }
        public long MeasurementId { get; set; }
        public string MeasurementName { get; set; } = string.Empty;
        public string MeasurementSymbol { get; set; } = string.Empty;
        public long ItemStatusTypeId { get; set; }
        public string StatusName { get; set; } = string.Empty;
        public DateOnly AcquisitionDate { get; set; }
        public DateOnly? ExpectedExpirationDate { get; set; }
        public string? SourceLocation { get; set; }
        public string? Notes { get; set; }
        public bool IsExpired { get; set; }
        public bool IsExpiringSoon { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime? LastModifiedDate { get; set; }
    }
}

using System;
using System.ComponentModel.DataAnnotations;

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

    /// <summary>
    /// Request model for creating a pantry item.
    /// </summary>
    public class PantryItemCreateModel
    {
        [Required]
        public long HouseholdId { get; set; }

        [Required]
        public long IngredientId { get; set; }

        [Required]
        public decimal Quantity { get; set; }

        [Required]
        public long MeasurementId { get; set; }

        public DateOnly? AcquisitionDate { get; set; }
        public DateOnly? ExpectedExpirationDate { get; set; }
        public string? SourceLocation { get; set; }
        public string? Notes { get; set; }
    }

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

    /// <summary>
    /// Represents an ingredient needed for upcoming meals minus what's in the pantry.
    /// </summary>
    public class ShoppingNeedModel
    {
        public long IngredientId { get; set; }
        public string IngredientName { get; set; } = string.Empty;
        public decimal QuantityNeeded { get; set; }
        public decimal QuantityOnHand { get; set; }
        public decimal QuantityToBuy { get; set; }
        public long MeasurementId { get; set; }
        public string MeasurementName { get; set; } = string.Empty;
        public string MeasurementSymbol { get; set; } = string.Empty;
        public string MeasurementCategory { get; set; } = string.Empty;
    }

    /// <summary>
    /// Response for the shopping needs endpoint.
    /// </summary>
    public class ShoppingNeedsResponseModel
    {
        public long HouseholdId { get; set; }
        public int DaysAhead { get; set; }
        public DateOnly FromDate { get; set; }
        public DateOnly ToDate { get; set; }
        public int MealCount { get; set; }
        public List<ShoppingNeedModel> Needs { get; set; } = new();
    }
}

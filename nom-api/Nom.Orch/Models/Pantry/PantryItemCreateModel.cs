using System;
using System.ComponentModel.DataAnnotations;

namespace Nom.Orch.Models.Pantry
{
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
}

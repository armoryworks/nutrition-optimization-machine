using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Nom.Orch.Models.Shopping
{
    public class HouseholdShoppingItemModel
    {
        public long Id { get; set; }
        public long HouseholdId { get; set; }
        public string Name { get; set; } = string.Empty;
        public long? IngredientId { get; set; }
        public decimal? Quantity { get; set; }
        public string? Unit { get; set; }
        public bool Checked { get; set; }
        public DateTime? CheckedDate { get; set; }
        public long? CheckedByPersonId { get; set; }
        public long? CreatedByPersonId { get; set; }
        public DateTime CreatedDate { get; set; }
        public string? Department { get; set; }
        public bool IngredientNeedsVerification { get; set; }
    }

    public class HouseholdShoppingItemCreateModel
    {
        [Required]
        public long HouseholdId { get; set; }

        [Required]
        [StringLength(255, MinimumLength = 1)]
        public string Name { get; set; } = string.Empty;

        public long? IngredientId { get; set; }
        public decimal? Quantity { get; set; }

        [StringLength(32)]
        public string? Unit { get; set; }
    }

    /// <summary>A null name leaves the name alone; quantity, unit and ingredient are replaced as sent, so null clears them.</summary>
    public class HouseholdShoppingItemUpdateModel
    {
        [StringLength(255, MinimumLength = 1)]
        public string? Name { get; set; }

        public decimal? Quantity { get; set; }

        [StringLength(32)]
        public string? Unit { get; set; }

        public long? IngredientId { get; set; }
    }

    public class ShoppingCheckModel
    {
        public string ItemKey { get; set; } = string.Empty;
        public DateTime CheckedDate { get; set; }
        public long? CheckedByPersonId { get; set; }
    }

    public class ShoppingStateModel
    {
        public List<HouseholdShoppingItemModel> Items { get; set; } = new();
        public List<ShoppingCheckModel> Checks { get; set; } = new();
    }

    public class ShoppingCheckRequestModel
    {
        [Required]
        public long HouseholdId { get; set; }

        [StringLength(64, MinimumLength = 1)]
        public string? ItemKey { get; set; }

        public long? ItemId { get; set; }

        public bool Checked { get; set; }
    }

    public class ShoppingCheckResultModel
    {
        public string ItemKey { get; set; } = string.Empty;
        public long? ItemId { get; set; }
        public bool Checked { get; set; }
        public DateTime? CheckedDate { get; set; }
        public NeedsVerificationModel? Verification { get; set; }
    }

    public class HouseholdRequestModel
    {
        [Required]
        public long HouseholdId { get; set; }
    }
}

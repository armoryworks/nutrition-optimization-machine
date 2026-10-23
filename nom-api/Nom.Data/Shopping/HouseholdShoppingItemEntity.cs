using System;
using Nom.Data.Person;
using Nom.Data.Plan;
using Nom.Data.Recipe;

namespace Nom.Data.Shopping
{
    /// <summary>
    /// An item a household member typed onto the shared shopping list by hand. Lines that
    /// come from the meal plan and the pantry are computed, never stored; only these ad-hoc
    /// lines persist. When the name matched or created a catalog food it is linked through
    /// <see cref="IngredientId"/>, which is what makes the label-scan prompt possible.
    /// </summary>
    public class HouseholdShoppingItemEntity : BaseEntity
    {
        public long HouseholdId { get; set; }
        public virtual HouseholdEntity? Household { get; set; }

        /// <summary>The name as typed, trimmed.</summary>
        public string Name { get; set; } = string.Empty;

        public long? IngredientId { get; set; }
        public virtual IngredientEntity? Ingredient { get; set; }

        public decimal? Quantity { get; set; }

        /// <summary>Free text; an ad-hoc line never feeds nutrition.</summary>
        public string? Unit { get; set; }

        public bool Checked { get; set; }
        public DateTime? CheckedDate { get; set; }

        public long? CheckedByPersonId { get; set; }
        public virtual PersonEntity? CheckedByPerson { get; set; }
    }
}

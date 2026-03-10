using System; // Required for DateOnly
using Nom.Data.Plan;     // Required for PlanEntity, HouseholdEntity
using Nom.Data.Recipe;    // Required for IngredientEntity
using Nom.Data.Reference; // Required for ItemStatusType
using Nom.Data.Measurement; // Required for Measurement

namespace Nom.Data.Shopping // Namespace remains Shopping
{
    /// <summary>
    /// Represents a single unit of an ingredient tracked within a user's inventory,
    /// serving both as an item on a shopping list and an item in a pantry.
    /// Its status indicates its current state (e.g., On List, Acquired, In Pantry, Used, Expired).
    /// Maps to the 'Shopping.pantry_item' table.
    /// </summary>
    public class PantryItemEntity : BaseEntity
    {
        /// <summary>
        /// Foreign key to the Household entity. Allows direct household scoping
        /// without navigating through Plan. Nullable for backward compatibility.
        /// </summary>
        public long? HouseholdId { get; set; }

        /// <summary>
        /// Navigation property to the associated HouseholdEntity.
        /// </summary>
        public virtual HouseholdEntity? Household { get; set; }

        /// <summary>
        /// Foreign key to the Plan entity this item is associated with.
        /// This represents the inventory (pantry stock and shopping list items) for a given plan.
        /// Corresponds to BIGINT NOT NULL.
        /// </summary>
        public long PlanId { get; set; }

        /// <summary>
        /// Navigation property to the associated PlanEntity.
        /// </summary>
        public virtual PlanEntity Plan { get; set; } = default!;

        /// <summary>
        /// Foreign key to the ShoppingTrip entity this item might be associated with (e.g., if it was
        /// part of a planned trip or acquired during a trip). Nullable if not linked to a specific trip.
        /// Corresponds to BIGINT NULL.
        /// </summary>
        public long? ShoppingTripId { get; set; }

        /// <summary>
        /// Navigation property to the associated ShoppingTripEntity (nullable).
        /// </summary>
        public virtual ShoppingTripEntity? ShoppingTrip { get; set; }

        /// <summary>
        /// Foreign key to the Recipe.Ingredient table, specifying what ingredient this item is.
        /// Corresponds to BIGINT NOT NULL.
        /// </summary>
        public long IngredientId { get; set; }

        /// <summary>
        /// Navigation property to the associated IngredientEntity.
        /// </summary>
        public virtual Recipe.IngredientEntity Ingredient { get; set; } = default!;

        /// <summary>
        /// The quantity of the item currently on hand or expected.
        /// Corresponds to DECIMAL NOT NULL.
        /// </summary>
        public decimal Quantity { get; set; }

        /// <summary>
        /// Foreign key to the Measurement.Measurement table, indicating the unit of measurement for the quantity
        /// (e.g., "grams", "units", "liters"). Corresponds to BIGINT NOT NULL.
        /// </summary>
        public long MeasurementId { get; set; }

        /// <summary>
        /// Navigation property to the associated MeasurementEntity.
        /// </summary>
        public virtual MeasurementEntity Measurement { get; set; } = default!;

        /// <summary>
        /// Foreign key to the Reference.reference table, indicating the current status of the item
        /// (e.g., "On Shopping List", "Acquired", "In Pantry", "Used", "Expired"). Corresponds to BIGINT NOT NULL.
        /// </summary>
        public long ItemStatusTypeId { get; set; }

        /// <summary>
        /// Navigation property to the associated ReferenceEntity representing the item's status type.
        /// </summary>
        public virtual ReferenceEntity ItemStatusType { get; set; } = default!;

        /// <summary>
        /// The date when the item was added to the inventory or acquired (if applicable).
        /// Corresponds to DATE NOT NULL.
        /// </summary>
        public DateOnly AcquisitionDate { get; set; }

        /// <summary>
        /// The estimated date when the item is expected to expire or go bad.
        /// Corresponds to DATE NULL.
        /// </summary>
        public DateOnly? ExpectedExpirationDate { get; set; }

        /// <summary>
        /// The location or store where the item was purchased (if acquired).
        /// Corresponds to VARCHAR(255) NULL.
        /// </summary>
        public string? SourceLocation { get; set; }

        /// <summary>
        /// Any additional relevant facts or notes about the item.
        /// Corresponds to VARCHAR(2047) NULL.
        /// </summary>
        public string? Notes { get; set; }
    }
}

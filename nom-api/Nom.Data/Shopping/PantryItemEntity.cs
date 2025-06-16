using System; // Required for DateOnly
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Nom.Data.Plan;     // Required for PlanEntity
using Nom.Data.Recipe;    // Required for IngredientEntity
using Nom.Data.Reference; // Required for MeasurementType, ItemStatusType

namespace Nom.Data.Shopping // Namespace remains Shopping
{
    /// <summary>
    /// Represents a single unit of an ingredient tracked within a user's inventory,
    /// serving both as an item on a shopping list and an item in a pantry.
    /// Its status indicates its current state (e.g., On List, Acquired, In Pantry, Used, Expired).
    /// Maps to the 'Shopping.pantry_item' table.
    /// </summary>
    [Table("PantryItem", Schema = "shopping")] // Table name capitalized, schema lowercase
    public class PantryItemEntity : BaseEntity
    {
        /// <summary>
        /// Foreign key to the Plan entity this item is associated with.
        /// This represents the inventory (pantry stock and shopping list items) for a given plan.
        /// Corresponds to BIGINT NOT NULL.
        /// </summary>
        [Required]
        public long PlanId { get; set; }

        /// <summary>
        /// Navigation property to the associated PlanEntity.
        /// </summary>
        [ForeignKey(nameof(PlanId))]
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
        [ForeignKey(nameof(ShoppingTripId))]
        public virtual ShoppingTripEntity? ShoppingTrip { get; set; }

        /// <summary>
        /// Foreign key to the Recipe.Ingredient table, specifying what ingredient this item is.
        /// Corresponds to BIGINT NOT NULL.
        /// </summary>
        [Required]
        public long IngredientId { get; set; }

        /// <summary>
        /// Navigation property to the associated IngredientEntity.
        /// </summary>
        [ForeignKey(nameof(IngredientId))]
        public virtual Recipe.IngredientEntity Ingredient { get; set; } = default!;

        /// <summary>
        /// The quantity of the item currently on hand or expected.
        /// Corresponds to DECIMAL NOT NULL.
        /// </summary>
        [Required]
        [Column(TypeName = "decimal(18,4)")] // Suitable precision for quantity
        public decimal Quantity { get; set; }

        /// <summary>
        /// Foreign key to the Reference.reference table, indicating the unit of measurement for the quantity
        /// (e.g., "grams", "units", "liters"). Corresponds to BIGINT NOT NULL.
        /// </summary>
        [Required]
        public long MeasurementTypeId { get; set; }

        /// <summary>
        /// Navigation property to the associated ReferenceEntity representing the measurement type.
        /// </summary>
        [ForeignKey(nameof(MeasurementTypeId))]
        public virtual ReferenceEntity MeasurementType { get; set; } = default!;

        /// <summary>
        /// Foreign key to the Reference.reference table, indicating the current status of the item
        /// (e.g., "On Shopping List", "Acquired", "In Pantry", "Used", "Expired"). Corresponds to BIGINT NOT NULL.
        /// </summary>
        [Required]
        public long ItemStatusTypeId { get; set; }

        /// <summary>
        /// Navigation property to the associated ReferenceEntity representing the item's status type.
        /// </summary>
        [ForeignKey(nameof(ItemStatusTypeId))]
        public virtual ReferenceEntity ItemStatusType { get; set; } = default!;

        /// <summary>
        /// The date when the item was added to the inventory or acquired (if applicable).
        /// Corresponds to DATE NOT NULL.
        /// </summary>
        [Required]
        [Column(TypeName = "date")]
        public DateOnly AcquisitionDate { get; set; }

        /// <summary>
        /// The estimated date when the item is expected to expire or go bad.
        /// Corresponds to DATE NULL.
        /// </summary>
        [Column(TypeName = "date")]
        public DateOnly? ExpectedExpirationDate { get; set; }

        /// <summary>
        /// The location or store where the item was purchased (if acquired).
        /// Corresponds to VARCHAR(255) NULL.
        /// </summary>
        [MaxLength(255)]
        public string? SourceLocation { get; set; }

        /// <summary>
        /// Any additional relevant facts or notes about the item.
        /// Corresponds to VARCHAR(2047) NULL.
        /// </summary>
        [MaxLength(2047)]
        public string? Notes { get; set; }
    }
}
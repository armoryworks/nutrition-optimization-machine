// Nom.Data/Nutrient/NutrientEntity.cs
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Nom.Data.Reference; // For MeasurementType reference

namespace Nom.Data.Nutrient
{
    /// <summary>
    /// Represents a distinct nutritional component (e.g., Protein, Vitamin C, Calcium).
    /// Maps to the 'Nutrient.nutrient' table.
    /// </summary>
    [Table("Nutrient", Schema = "nutrient")]
    public class NutrientEntity : BaseEntity
    {
        /// <summary>
        /// The unique name of the nutrient (e.g., "Protein", "Vitamin C").
        /// Corresponds to VARCHAR(255) NOT NULL.
        /// </summary>
        [Required]
        [MaxLength(255)]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// A brief description of the nutrient's function or source.
        /// Corresponds to VARCHAR(1023) NULLABLE.
        /// </summary>
        [MaxLength(1023)]
        public string? Description { get; set; }

        /// <summary>
        /// Foreign key to the Reference.Reference table, indicating the default measurement unit for this nutrient (e.g., "g", "mg", "mcg").
        /// This is the unit in which the nutrient amount is typically expressed.
        /// Corresponds to BIGINT NOT NULL.
        /// </summary>
        public long DefaultMeasurementTypeId { get; set; }

        /// <summary>
        /// Navigation property to the associated default MeasurementType.
        /// </summary>
        [ForeignKey(nameof(DefaultMeasurementTypeId))]
        public virtual ReferenceEntity DefaultMeasurementType { get; set; } = default!;

        /// <summary>
        /// Foreign key to a parent NutrientEntity, if this nutrient is a component of another (e.g., "Saturated Fat" is a child of "Fat").
        /// Corresponds to BIGINT NULLABLE.
        /// </summary>
        public long? ParentNutrientId { get; set; }

        /// <summary>
        /// Navigation property to the parent NutrientEntity.
        /// </summary>
        [ForeignKey(nameof(ParentNutrientId))]
        public virtual NutrientEntity? ParentNutrient { get; set; }

        /// <summary>
        /// Collection of child nutrients (e.g., "Fat" has "Saturated Fat" as a child).
        /// </summary>
        public virtual ICollection<NutrientEntity> ChildNutrients { get; set; } = new List<NutrientEntity>();

        /// <summary>
        /// The FoodData Central (FDC) ID for this nutrient, if it originated from FDC data.
        /// Useful for traceability and linking back to the FDC database.
        /// Corresponds to VARCHAR(50) NULLABLE.
        /// </summary>
        [MaxLength(50)] // FDC nutrient IDs are integers, but stored as string to match general FdcId pattern
        public string? FdcId { get; set; }

        // Navigation properties
        public virtual ICollection<IngredientNutrientEntity> IngredientNutrients { get; set; } = new List<IngredientNutrientEntity>();
        public virtual ICollection<NutrientGuidelineEntity> Guidelines { get; set; } = new List<NutrientGuidelineEntity>();
    }
}

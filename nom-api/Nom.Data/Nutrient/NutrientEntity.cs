// Nom.Data/Nutrient/NutrientEntity.cs
using System.Collections.Generic;
using Nom.Data.Reference; // For other references
using Nom.Data.Measurement; // For Measurement reference

namespace Nom.Data.Nutrient
{
    /// <summary>
    /// Represents a distinct nutritional component (e.g., Protein, Vitamin C, Calcium).
    /// Maps to the 'Nutrient.nutrient' table.
    /// </summary>
    public class NutrientEntity : BaseEntity
    {
        /// <summary>
        /// The unique name of the nutrient (e.g., "Protein", "Vitamin C").
        /// Corresponds to VARCHAR(255) NOT NULL.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// A brief description of the nutrient's function or source.
        /// Corresponds to VARCHAR(1023) NULLABLE.
        /// </summary>
        public string? Description { get; set; }

        public decimal? Rank { get; set; }

        /// <summary>
        /// Foreign key to the Measurement.Measurement table, indicating the default measurement unit for this nutrient (e.g., "g", "mg", "mcg").
        /// This is the unit in which the nutrient amount is typically expressed.
        /// Corresponds to BIGINT NOT NULL.
        /// </summary>
        public long DefaultMeasurementId { get; set; }

        /// <summary>
        /// Navigation property to the associated default MeasurementEntity.
        /// </summary>
        public virtual MeasurementEntity DefaultMeasurement { get; set; } = default!;

        /// <summary>
        /// Foreign key to a parent NutrientEntity, if this nutrient is a component of another (e.g., "Saturated Fat" is a child of "Fat").
        /// Corresponds to BIGINT NULLABLE.
        /// </summary>
        public long? ParentNutrientId { get; set; }

        /// <summary>
        /// Navigation property to the parent NutrientEntity.
        /// </summary>
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
        public string? FdcId { get; set; }

        /// <summary>
        /// Indicates whether this nutrient is a micronutrient (vitamins, minerals) vs macronutrient (protein, carbs, fat).
        /// </summary>
        public bool? IsMicronutrient { get; set; } = false;

        // Navigation properties
        public virtual ICollection<IngredientNutrientEntity> IngredientNutrients { get; set; } = new List<IngredientNutrientEntity>();
        public virtual ICollection<NutrientGuidelineEntity> Guidelines { get; set; } = new List<NutrientGuidelineEntity>();
    }
}

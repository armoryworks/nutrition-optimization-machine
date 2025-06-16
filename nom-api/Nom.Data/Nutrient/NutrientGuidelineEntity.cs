using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Nom.Data.Reference; // Required for GuidelineBasisType and MeasurementType navigation properties

namespace Nom.Data.Nutrient
{
    /// <summary>
    /// Represents a nutritional guideline (e.g., Recommended Daily Allowance, Upper Limit)
    /// for a specific nutrient.
    /// Maps to the 'Nutrient.nutrient_guideline' table.
    /// </summary>
    [Table("NutrientGuideline", Schema = "nutrient")] // Table name capitalized, schema lowercase
    public class NutrientGuidelineEntity : BaseEntity
    {
        /// <summary>
        /// Foreign key to the Reference.reference table, indicating the basis for the guideline
        /// (e.g., "RDA" - Recommended Daily Allowance, "AI" - Adequate Intake, "UL" - Tolerable Upper Intake Level).
        /// Corresponds to BIGINT NOT NULL.
        /// </summary>
        public long GuidelineBasisTypeId { get; set; }

        /// <summary>
        /// Navigation property to the associated ReferenceEntity representing the guideline basis type.
        /// </summary>
        [ForeignKey(nameof(GuidelineBasisTypeId))]
        public virtual ReferenceEntity GuidelineBasisType { get; set; } = default!; // Required navigation property

        /// <summary>
        /// Foreign key to the Reference.reference table, indicating the unit of measurement
        /// for the minimum and maximum values (e.g., "mg", "g", "IU"). Corresponds to BIGINT NOT NULL.
        /// </summary>
        public long MeasurementTypeId { get; set; }

        /// <summary>
        /// Navigation property to the associated ReferenceEntity representing the measurement type.
        /// </summary>
        [ForeignKey(nameof(MeasurementTypeId))]
        public virtual ReferenceEntity MeasurementType { get; set; } = default!; // Required navigation property

        /// <summary>
        /// The minimum recommended or allowed measurement for the nutrient.
        /// Corresponds to DECIMAL NOT NULL.
        /// </summary>
        [Required]
        [Column(TypeName = "decimal(18,4)")] // Using a common decimal precision/scale, adjust if needed
        public decimal MinimumMeasurement { get; set; }

        /// <summary>
        /// The maximum recommended or allowed measurement for the nutrient.
        /// Corresponds to DECIMAL NOT NULL.
        /// </summary>
        [Required]
        [Column(TypeName = "decimal(18,4)")] // Using a common decimal precision/scale, adjust if needed
        public decimal MaximumMeasurement { get; set; }
    }
}
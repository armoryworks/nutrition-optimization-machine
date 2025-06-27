// Nom.Data/Nutrient/NutrientGuidelineEntity.cs
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Nom.Data.Reference; // Required for MeasurementType navigation property
using System; // For DateTime

namespace Nom.Data.Nutrient
{
    /// <summary>
    /// Represents a nutritional guideline (e.g., Recommended Daily Allowance, Upper Limit, Acceptable Macronutrient Distribution Range)
    /// for a specific nutrient, applicable to a specific goal/demographic type.
    /// Maps to the 'nutrient.NutrientGuideline' table.
    /// </summary>
    [Table("NutrientGuideline", Schema = "nutrient")]
    public class NutrientGuidelineEntity : BaseEntity
    {
        /// <summary>
        /// Foreign key to the Nutrient.Nutrient table, identifying the nutrient this guideline is for.
        /// Corresponds to BIGINT NOT NULL.
        /// </summary>
        public long NutrientId { get; set; }

        /// <summary>
        /// Navigation property to the associated NutrientEntity.
        /// </summary>
        [ForeignKey(nameof(NutrientId))]
        public virtual NutrientEntity Nutrient { get; set; } = default!; // Required navigation property

        /// <summary>
        /// Foreign key to the Reference.Reference table (with GoalType discriminator),
        /// indicating the demographic group or goal type this guideline applies to (e.g., "Adults >= 4 years", "Pregnant Women").
        /// Corresponds to BIGINT NOT NULL.
        /// </summary>
        public long GoalTypeId { get; set; }

        /// <summary>
        /// Navigation property to the associated ReferenceEntity representing the goal/demographic type.
        /// </summary>
        [ForeignKey(nameof(GoalTypeId))]
        public virtual ReferenceEntity GoalType { get; set; } = default!; // Required navigation property

        /// <summary>
        /// Foreign key to the Reference.Reference table (with MeasurementType discriminator),
        /// indicating the unit of measurement for the guideline amounts (e.g., "mg", "g", "mcg", "kcal").
        /// Corresponds to BIGINT NOT NULL.
        /// </summary>
        public long MeasurementTypeId { get; set; }

        /// <summary>
        /// Navigation property to the associated ReferenceEntity representing the measurement type.
        /// </summary>
        [ForeignKey(nameof(MeasurementTypeId))]
        public virtual ReferenceEntity MeasurementType { get; set; } = default!; // Required navigation property

        /// <summary>
        /// The minimum recommended or allowed amount for the nutrient (e.g., EAR, or lower bound of AMDR).
        /// Can be null if not applicable for this guideline.
        /// Corresponds to DECIMAL(18,4) NULLABLE.
        /// </summary>
        [Column(TypeName = "decimal(18,4)")] // Using a common decimal precision/scale, adjust if needed
        public decimal? MinAmount { get; set; }

        /// <summary>
        /// The maximum recommended or allowed amount for the nutrient (e.g., UL - Tolerable Upper Intake Level, or upper bound of AMDR).
        /// Can be null if not applicable for this guideline.
        /// Corresponds to DECIMAL(18,4) NULLABLE.
        /// </summary>
        [Column(TypeName = "decimal(18,4)")] // Using a common decimal precision/scale, adjust if needed
        public decimal? MaxAmount { get; set; }

        /// <summary>
        /// The primary recommended daily intake amount (e.g., RDA or AI).
        /// Can be null if the guideline is solely a min/max range (like some AMDRs) or an UL.
        /// Corresponds to DECIMAL(18,4) NULLABLE.
        /// </summary>
        [Column(TypeName = "decimal(18,4)")]
        public decimal? RecommendedAmount { get; set; }

        /// <summary>
        /// A descriptive message providing more context about the guideline,
        /// such as its basis (RDA, AI, UL, AMDR) or specific footnotes from the source document.
        /// </summary>
        [MaxLength(500)]
        public string? Notes { get; set; }
    }
}

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
        /// </summary>
        public long NutrientId { get; set; }

        /// <summary>
        /// Navigation property to the associated NutrientEntity.
        /// </summary>
        [ForeignKey(nameof(NutrientId))]
        public virtual NutrientEntity Nutrient { get; set; } = default!;

        /// <summary>
        /// Foreign key to the Reference.Reference table (with GoalType discriminator),
        /// This will contain any number of goals. For imported data from fdc, the goal type will be "FDC Guideline Compliance"
        /// </summary>
        [Required]
        public long GoalTypeId { get; set; }

        /// <summary>
        /// Navigation property to the associated ReferenceEntity representing the goal/demographic type.
        /// </summary>
        [ForeignKey(nameof(GoalTypeId))]
        public virtual ReferenceEntity GoalType { get; set; } = default!;

        /// <summary>
        /// Foreign key to the Reference.Reference table (with MeasurementType discriminator),
        /// indicating the unit of measurement for the guideline amounts (e.g., "mg", "g", "mcg", "kcal").
        /// </summary>
        public long MeasurementTypeId { get; set; }

        /// <summary>
        /// Navigation property to the associated ReferenceEntity representing the measurement type.
        /// </summary>
        [ForeignKey(nameof(MeasurementTypeId))]
        public virtual ReferenceEntity MeasurementType { get; set; } = default!;

        /// <summary>
        /// The minimum recommended or allowed amount for the nutrient (e.g., EAR, or lower bound of AMDR).
        /// </summary>
        [Column(TypeName = "decimal(18,4)")]
        public decimal? MinAmount { get; set; }

        /// <summary>
        /// The maximum recommended or allowed amount for the nutrient (e.g., UL - Tolerable Upper Intake Level, or upper bound of AMDR).
        /// </summary>
        [Column(TypeName = "decimal(18,4)")]
        public decimal? MaxAmount { get; set; }

        /// <summary>
        /// The primary recommended daily intake amount (e.g., RDA or AI).
        /// </summary>
        [Column(TypeName = "decimal(18,4)")]
        public decimal? RecommendedAmount { get; set; }

        /// <summary>
        /// A descriptive message providing more context about the guideline,
        /// such as its basis (RDA, AI, UL, AMDR) or specific footnotes from the source document.
        /// </summary>
        [Column(TypeName = "text")]
        public string? Notes { get; set; }
    }
}
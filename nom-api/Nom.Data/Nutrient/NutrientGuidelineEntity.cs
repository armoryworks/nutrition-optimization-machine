using Nom.Data.Reference; // Required for GoalType navigation property
using Nom.Data.Measurement; // Required for Measurement navigation property
using System; // For DateTime

namespace Nom.Data.Nutrient
{
    /// <summary>
    /// Represents a nutritional guideline (e.g., Recommended Daily Allowance, Upper Limit, Acceptable Macronutrient Distribution Range)
    /// for a specific nutrient, applicable to a specific goal/demographic type.
    /// Maps to the 'nutrient.NutrientGuideline' table.
    /// </summary>
    public class NutrientGuidelineEntity : BaseEntity
    {
        /// <summary>
        /// Foreign key to the Nutrient.Nutrient table, identifying the nutrient this guideline is for.
        /// </summary>
        public long NutrientId { get; set; }

        /// <summary>
        /// Navigation property to the associated NutrientEntity.
        /// </summary>
        public virtual NutrientEntity Nutrient { get; set; } = default!;

        /// <summary>
        /// Foreign key to the Reference.Reference table (with GoalType discriminator),
        /// This will contain any number of goals. For imported data from fdc, the goal type will be "FDC Guideline Compliance"
        /// </summary>
        public long GoalTypeId { get; set; }

        /// <summary>
        /// Navigation property to the associated ReferenceEntity representing the goal/demographic type.
        /// </summary>
        public virtual ReferenceEntity GoalType { get; set; } = default!;

        /// <summary>
        /// Foreign key to the Measurement.Measurement table,
        /// indicating the unit of measurement for the guideline amounts (e.g., "mg", "g", "mcg", "kcal").
        /// </summary>
        public long MeasurementId { get; set; }

        /// <summary>
        /// Navigation property to the associated MeasurementEntity.
        /// </summary>
        public virtual MeasurementEntity Measurement { get; set; } = default!;

        /// <summary>
        /// The minimum recommended or allowed amount for the nutrient (e.g., EAR, or lower bound of AMDR).
        /// </summary>
        public decimal? MinAmount { get; set; }

        /// <summary>
        /// The maximum recommended or allowed amount for the nutrient (e.g., UL - Tolerable Upper Intake Level, or upper bound of AMDR).
        /// </summary>
        public decimal? MaxAmount { get; set; }

        /// <summary>
        /// The primary recommended daily intake amount (e.g., RDA or AI).
        /// </summary>
        public decimal? RecommendedAmount { get; set; }

        /// <summary>
        /// A descriptive message providing more context about the guideline,
        /// such as its basis (RDA, AI, UL, AMDR) or specific footnotes from the source document.
        /// </summary>
        public string? Notes { get; set; }
    }
}
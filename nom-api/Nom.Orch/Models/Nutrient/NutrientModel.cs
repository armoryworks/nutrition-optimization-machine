using System;

namespace Nom.Orch.Models.Nutrient
{
    /// <summary>
    /// Model representing a nutrient for API responses.
    /// </summary>
    public class NutrientModel
    {
        /// <summary>
        /// The unique identifier of the nutrient.
        /// </summary>
        public long Id { get; set; }

        /// <summary>
        /// The name of the nutrient (e.g., "Protein", "Vitamin C").
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// A brief description of the nutrient's function or source.
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// The ID of the default measurement unit for this nutrient.
        /// </summary>
        public long DefaultMeasurementId { get; set; }

        /// <summary>
        /// The name of the default measurement unit.
        /// </summary>
        public string DefaultMeasurementName { get; set; } = string.Empty;

        /// <summary>
        /// The symbol of the default measurement unit (e.g., "g", "mg", "mcg").
        /// </summary>
        public string DefaultMeasurementSymbol { get; set; } = string.Empty;

        /// <summary>
        /// The rank/priority of the nutrient for display purposes.
        /// </summary>
        public decimal? Rank { get; set; }

        /// <summary>
        /// The date when the nutrient was created.
        /// </summary>
        public DateTime CreatedDate { get; set; }

        /// <summary>
        /// The date when the nutrient was last modified.
        /// </summary>
        public DateTime? LastModifiedDate { get; set; }
    }
}

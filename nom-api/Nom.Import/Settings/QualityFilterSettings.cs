// File: nom-api/Nom.Import/Settings/QualityFilterSettings.cs

namespace Nom.Import.Settings
{
    /// <summary>
    /// Settings for data quality filtering and scoring.
    /// </summary>
    public class QualityFilterSettings
    {
        /// <summary>
        /// Minimum number of data points required for nutrient values.
        /// </summary>
        public int MinimumDataPoints { get; set; } = 1;

        /// <summary>
        /// Minimum year for data acquisition (for freshness).
        /// </summary>
        public int MinimumYearAcquired { get; set; } = 2010;

        /// <summary>
        /// Whether to prioritize foundation foods over branded foods.
        /// </summary>
        public bool PrioritizeFoundationFoods { get; set; } = true;

        /// <summary>
        /// Maximum length for ingredient names (to filter out overly long names).
        /// </summary>
        public int MaximumIngredientNameLength { get; set; } = 200;

        /// <summary>
        /// Quality scoring weights for different factors.
        /// </summary>
        public QualityWeights Weights { get; set; } = new();
    }
} 
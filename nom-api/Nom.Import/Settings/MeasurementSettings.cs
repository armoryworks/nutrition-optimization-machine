// File: nom-api/Nom.Import/Settings/MeasurementSettings.cs

namespace Nom.Import.Settings
{
    /// <summary>
    /// Settings for measurement unit system integration.
    /// </summary>
    public class MeasurementSettings
    {
        /// <summary>
        /// Whether to import measurement units.
        /// </summary>
        public bool ImportMeasurementUnits { get; set; } = true;

        /// <summary>
        /// Whether to import food portions.
        /// </summary>
        public bool ImportFoodPortions { get; set; } = true;

        /// <summary>
        /// Whether to calculate gram weights.
        /// </summary>
        public bool CalculateGramWeights { get; set; } = true;

        /// <summary>
        /// Whether to create conversion factors.
        /// </summary>
        public bool CreateConversionFactors { get; set; } = true;
    }
} 
// File: nom-api/Nom.Import/Settings/QualityWeights.cs

namespace Nom.Import.Settings
{
    /// <summary>
    /// Quality scoring weights for different factors.
    /// </summary>
    public class QualityWeights
    {
        public double DataPoints { get; set; } = 0.3;
        public double DataFreshness { get; set; } = 0.2;
        public double FoodType { get; set; } = 0.3;
        public double NameQuality { get; set; } = 0.2;
    }
} 
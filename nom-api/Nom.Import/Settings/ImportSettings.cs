// File: nom-api/Nom.Import/Settings/ImportSettings.cs

namespace Nom.Import.Settings
{
    /// <summary>
    /// Represents the settings required for the data import process,
    /// typically configured in appsettings.json.
    /// </summary>
    public class ImportSettings
    {
        /// <summary>
        /// The directory path where the source CSV files for the import are located.
        /// </summary>
        public string SourceDirectory { get; set; } = string.Empty;

        /// <summary>
        /// The PostgreSQL connection string used by the bulk-copy importer.
        /// Bound from ConnectionStrings:NomConnection.
        /// </summary>
        public string ConnectionString { get; set; } = string.Empty;

        /// <summary>
        /// Configuration for data quality filtering and scoring.
        /// </summary>
        public QualityFilterSettings QualityFilter { get; set; } = new();

        /// <summary>
        /// Configuration for data source selection and prioritization.
        /// </summary>
        public DataSourceSettings DataSources { get; set; } = new();

        /// <summary>
        /// Configuration for performance and batch processing.
        /// </summary>
        public PerformanceSettings Performance { get; set; } = new();

        /// <summary>
        /// Configuration for measurement unit system integration.
        /// </summary>
        public MeasurementSettings Measurement { get; set; } = new();

        /// <summary>
        /// Configuration for recipe import and categorization.
        /// </summary>
        public RecipeSettings Recipe { get; set; } = new();

        /// <summary>
        /// Configuration for AI-powered ingredient enhancement.
        /// </summary>
        public AiEnhancementSettings AiEnhancement { get; set; } = new();
    }
} 
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

    /// <summary>
    /// Settings for data source selection and prioritization.
    /// </summary>
    public class DataSourceSettings
    {
        /// <summary>
        /// Whether to import foundation foods.
        /// </summary>
        public bool ImportFoundationFoods { get; set; } = true;

        /// <summary>
        /// Whether to import branded foods.
        /// </summary>
        public bool ImportBrandedFoods { get; set; } = false;

        /// <summary>
        /// Whether to import survey foods (SR legacy foods).
        /// </summary>
        public bool ImportSurveyFoods { get; set; } = true;

        /// <summary>
        /// Whether to import recipes.
        /// </summary>
        public bool ImportRecipes { get; set; } = true;

        /// <summary>
        /// Whether to import measurement units and portions.
        /// </summary>
        public bool ImportMeasurements { get; set; } = true;

        /// <summary>
        /// Whether to import food categories.
        /// </summary>
        public bool ImportFoodCategories { get; set; } = true;

        /// <summary>
        /// Whether to import food attributes.
        /// </summary>
        public bool ImportFoodAttributes { get; set; } = false;

        /// <summary>
        /// Maximum number of ingredients to import (0 = unlimited).
        /// </summary>
        public int MaxIngredientsToImport { get; set; } = 0;

        /// <summary>
        /// Maximum number of recipes to import (0 = unlimited).
        /// </summary>
        public int MaxRecipesToImport { get; set; } = 0;
    }

    /// <summary>
    /// Settings for performance and batch processing.
    /// </summary>
    public class PerformanceSettings
    {
        /// <summary>
        /// Batch size for processing large datasets.
        /// </summary>
        public int BatchSize { get; set; } = 10000;

        /// <summary>
        /// Whether to use parallel processing for large imports.
        /// </summary>
        public bool UseParallelProcessing { get; set; } = true;

        /// <summary>
        /// Maximum degree of parallelism for parallel processing.
        /// </summary>
        public int MaxDegreeOfParallelism { get; set; } = Environment.ProcessorCount;

        /// <summary>
        /// Whether to create indexes after import for better performance.
        /// </summary>
        public bool CreateIndexesAfterImport { get; set; } = true;

        /// <summary>
        /// Whether to create materialized views for common queries.
        /// </summary>
        public bool CreateMaterializedViews { get; set; } = true;
    }

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
        /// Whether to calculate gram weights for all portions.
        /// </summary>
        public bool CalculateGramWeights { get; set; } = true;

        /// <summary>
        /// Whether to create conversion factors between units.
        /// </summary>
        public bool CreateConversionFactors { get; set; } = true;
    }

    /// <summary>
    /// Settings for recipe import and categorization.
    /// </summary>
    public class RecipeSettings
    {
        /// <summary>
        /// Whether to import recipes.
        /// </summary>
        public bool ImportRecipes { get; set; } = true;

        /// <summary>
        /// Whether to categorize recipes by food category.
        /// </summary>
        public bool CategorizeRecipes { get; set; } = true;

        /// <summary>
        /// Whether to extract ingredients from recipe NER data.
        /// </summary>
        public bool ExtractIngredientsFromNER { get; set; } = true;

        /// <summary>
        /// Whether to map recipe ingredients to existing ingredients.
        /// </summary>
        public bool MapRecipeIngredients { get; set; } = true;

        /// <summary>
        /// Maximum number of ingredients per recipe to process.
        /// </summary>
        public int MaxIngredientsPerRecipe { get; set; } = 20;
    }

    /// <summary>
    /// Settings for AI-powered ingredient enhancement.
    /// </summary>
    public class AiEnhancementSettings
    {
        /// <summary>
        /// Whether to enable AI enhancement of ingredients.
        /// </summary>
        public bool EnableAiEnhancement { get; set; } = false;

        /// <summary>
        /// The AI service provider to use for enhancement.
        /// Options: OpenAI, Anthropic, GoogleGemini, AzureOpenAI, Ollama
        /// </summary>
        public string AiProvider { get; set; } = "OpenAI";

        /// <summary>
        /// Batch size for AI processing (to avoid rate limits).
        /// </summary>
        public int BatchSize { get; set; } = 10;

        /// <summary>
        /// Delay between batches in milliseconds (to avoid rate limits).
        /// </summary>
        public int BatchDelayMs { get; set; } = 1000;

        /// <summary>
        /// Whether to preserve original names as aliases.
        /// </summary>
        public bool PreserveOriginalNamesAsAliases { get; set; } = true;

        /// <summary>
        /// Whether to update ingredient descriptions with AI-enhanced descriptions.
        /// </summary>
        public bool UpdateDescriptions { get; set; } = true;

        /// <summary>
        /// Whether to update ingredient names with AI-enhanced names.
        /// </summary>
        public bool UpdateNames { get; set; } = true;

        /// <summary>
        /// Maximum number of ingredients to process (0 = unlimited).
        /// </summary>
        public int MaxIngredientsToProcess { get; set; } = 0;

        /// <summary>
        /// Quality threshold for AI enhancement (0.0 to 1.0).
        /// Ingredients below this threshold will not be enhanced.
        /// </summary>
        public double QualityThreshold { get; set; } = 0.5;
    }
}

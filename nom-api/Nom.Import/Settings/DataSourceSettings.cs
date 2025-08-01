// File: nom-api/Nom.Import/Settings/DataSourceSettings.cs

namespace Nom.Import.Settings
{
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
        /// Whether to import survey foods.
        /// </summary>
        public bool ImportSurveyFoods { get; set; } = true;

        /// <summary>
        /// Whether to import recipes.
        /// </summary>
        public bool ImportRecipes { get; set; } = true;

        /// <summary>
        /// Whether to import measurements.
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
} 
// Nom.Orch/Models/Recipe/KaggleRawRecipeDataModel.cs

namespace Nom.Orch.Models.Recipe
{
    /// <summary>
    /// Represents the raw data structure of a single recipe row as read directly from the Kaggle CSV.
    /// This model is used for initial deserialization before parsing into entities.
    /// </summary>
    public class KaggleRawRecipeDataModel
    {
        /// <summary>
        /// The title/name of the recipe from the CSV.
        /// Corresponds to the "Title" column.
        /// </summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// The raw, unparsed string of ingredients from the CSV.
        /// Corresponds to the "Ingredients" column (e.g., "[1 cup flour, 2 eggs]").
        /// </summary>
        public string Ingredients { get; set; } = string.Empty;

        /// <summary>
        /// The raw, unparsed instruction string from the CSV.
        /// Corresponds to the "Instructions" column.
        /// </summary>
        public string Instructions { get; set; } = string.Empty;

        /// <summary>
        /// Optional: Cooking time in seconds, if available in the Kaggle dataset.
        /// (e.g., from Food.com dataset's "Cooking Time in Seconds" column).
        /// </summary>
        public int? CookTimeSeconds { get; set; }

        /// <summary>
        /// Optional: Preparation time in seconds, if available in the Kaggle dataset.
        /// (e.g., from Food.com dataset's "Preparation Time in Seconds" column).
        /// Note: Kaggle data might also have "Preparation Time in Minutes", adjust accordingly.
        /// </summary>
        public int? PrepTimeSeconds { get; set; }

        /// <summary>
        /// Optional: Number of servings, if available in the Kaggle dataset.
        /// (e.g., from Food.com dataset's "Servings" column).
        /// </summary>
        public int? ServingsCount { get; set; }

        // Add other properties here if your specific Kaggle CSV has additional columns
        // that you wish to capture and process (e.g., 'Cuisine', 'Rating', 'Image_Name').
        // public string? Cuisine { get; set; }
        // public decimal? Rating { get; set; }
        // public string? ImageName { get; set; }
    }
}

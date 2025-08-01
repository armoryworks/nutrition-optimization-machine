// File: nom-api/Nom.Import/Settings/RecipeSettings.cs

namespace Nom.Import.Settings
{
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
        /// Whether to categorize recipes.
        /// </summary>
        public bool CategorizeRecipes { get; set; } = true;

        /// <summary>
        /// Whether to extract ingredients from NER.
        /// </summary>
        public bool ExtractIngredientsFromNER { get; set; } = true;

        /// <summary>
        /// Whether to map recipe ingredients.
        /// </summary>
        public bool MapRecipeIngredients { get; set; } = true;

        /// <summary>
        /// Maximum number of ingredients per recipe.
        /// </summary>
        public int MaxIngredientsPerRecipe { get; set; } = 20;
    }
} 
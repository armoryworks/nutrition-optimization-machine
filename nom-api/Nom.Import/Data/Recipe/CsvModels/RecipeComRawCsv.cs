using CsvHelper.Configuration.Attributes;

namespace Nom.Import.Data.Recipe.CsvModels
{
    /// <summary>
    /// Represents a row from the raw recipe CSV file (e.g., recipe.com data).
    /// Assumes 'ingredients' and 'directions' are JSON array strings.
    /// </summary>
    public class RecipeComRawCsv
    {
        [Name("blank_col")] // Matches the first column in your \copy command
        public string? BlankCol { get; set; } // Can be null if it's truly blank

        [Name("title")]
        public string Title { get; set; } = string.Empty;

        [Name("link")]
        public string Link { get; set; } = string.Empty;

        [Name("source")]
        public string Source { get; set; } = string.Empty;

        [Name("ingredients")]
        public string IngredientsJson { get; set; } = string.Empty; // Raw JSON string for ingredients

        [Name("directions")]
        public string DirectionsJson { get; set; } = string.Empty; // Raw JSON string for directions

        [Name("ner")] // Matches the last column in your \copy command
        public string? Ner { get; set; } // Can be null if not always present or used
    }
}

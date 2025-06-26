// Nom.Orch/Interfaces/IIngredientParsingService.cs
using Nom.Data.Recipe; // To reference IngredientEntity and RecipeIngredientEntity
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Nom.Orch.UtilityInterfaces
{
    /// <summary>
    /// Defines the contract for a service responsible for parsing raw ingredient strings
    /// into structured ingredient entities and their quantities/units.
    /// </summary>
    public interface IIngredientParsingService
    {
        /// <summary>
        /// Parses a single raw ingredient line (e.g., "1/2 cup all-purpose flour")
        /// and returns a structured representation, associating with existing or new IngredientEntities.
        /// </summary>
        /// <param name="rawIngredientLine">The raw string representing one ingredient line from a recipe.</param>
        /// <returns>
        /// A tuple containing:
        /// - RecipeIngredientEntity: The structured ingredient with quantity and unit.
        /// - IngredientEntity: The associated standardized IngredientEntity (could be new or existing).
        /// Returns null if parsing fails or input is invalid.
        /// </returns>
        Task<(RecipeIngredientEntity? RecipeIngredient, IngredientEntity? StandardizedIngredient)> ParseAndStandardizeIngredientAsync(string rawIngredientLine);

        /// <summary>
        /// Parses a collection of raw ingredient lines and returns structured representations.
        /// </summary>
        /// <param name="rawIngredientLines">A collection of raw ingredient strings (e.g., from a CSV).</param>
        /// <returns>
        /// A list of tuples, each containing a structured RecipeIngredientEntity and its associated StandardizedIngredient.
        /// Only successfully parsed ingredients are returned.
        /// </returns>
        Task<List<(RecipeIngredientEntity RecipeIngredient, IngredientEntity StandardizedIngredient)>> ParseAndStandardizeIngredientsAsync(string rawIngredientLines);
    }
}

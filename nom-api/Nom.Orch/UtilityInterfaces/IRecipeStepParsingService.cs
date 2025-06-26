// Nom.Orch/Interfaces/IRecipeStepParsingService.cs
using Nom.Data.Recipe; // To reference RecipeStepEntity
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Nom.Orch.UtilityInterfaces
{
    /// <summary>
    /// Defines the contract for a service responsible for parsing raw recipe instructions
    /// into a collection of ordered RecipeStepEntity objects.
    /// </summary>
    public interface IRecipeStepParsingService
    {
        /// <summary>
        /// Parses a raw, multi-step instruction string into an ordered list of RecipeStepEntity objects.
        /// </summary>
        /// <param name="rawInstructions">The complete raw instruction string from a recipe.</param>
        /// <returns>A list of parsed and ordered RecipeStepEntity objects.</returns>
        Task<List<RecipeStepEntity>> ParseInstructionsIntoStepsAsync(string rawInstructions);
    }
}

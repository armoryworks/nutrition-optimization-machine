using System.Collections.Generic;
using System.Threading.Tasks;
using Nom.Orch.UtilityInterfaces;

namespace Nom.Orch.Interfaces
{
    /// <summary>
    /// Plausibility vetting for imported recipes before they enter the library.
    /// A clean pass goes to the normal curation queue; any issues route the
    /// recipe to admin review (RequiresRevision) with the issues recorded.
    /// The current implementation is rules-based; the contract is async and
    /// content-driven so an LLM-backed vetter can replace it without callers
    /// changing.
    /// </summary>
    public interface IRecipeVettingService
    {
        /// <summary>Returns human-readable issues; empty means the recipe looks plausible.</summary>
        Task<List<string>> VetAsync(ScraperRecipe recipe);
    }
}

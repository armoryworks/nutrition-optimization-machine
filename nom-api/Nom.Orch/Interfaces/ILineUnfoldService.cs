using System.Threading.Tasks;
using Nom.Orch.Models.Curation;

namespace Nom.Orch.Interfaces
{
    /// <summary>
    /// Admin repair of scraped recipe rows that hold several source lines joined with " + "
    /// ("1 3/4 cup sugar + 1/3 cup sugar" counted as 1 3/4 cup). Each line becomes its own row
    /// on the same food. Deterministic text rules only, every row taken apart is written to the
    /// audit log, and it can be put back together from there.
    /// </summary>
    public interface ILineUnfoldService
    {
        /// <summary>
        /// Takes up to <paramref name="maxRows"/> joined rows apart: the row keeps its place, its
        /// amount and its first line, and each further line is added at the end of the recipe with
        /// the amount the line parser reads from it. A row that is one line of the recipe's source
        /// list is left alone, as is a row classified as something other than an ingredient.
        /// Without <paramref name="apply"/> nothing is written and the result is what the same
        /// call would do.
        /// </summary>
        Task<LineUnfoldResult> UnfoldAsync(bool apply = false, int maxRows = 5000, int sampleSize = 50);
    }
}

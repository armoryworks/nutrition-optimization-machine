using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Nom.Orch.Interfaces
{
    /// <summary>
    /// The model behind the RequiresRevision repair lane. It only proposes: where a one-paragraph
    /// method divides into steps, and which verbatim phrase states an ingredient's amount. Every
    /// proposal is checked by <see cref="Nom.Orch.Services.Support.RecipeRepairGrounding"/> before use.
    /// </summary>
    public interface IRecipeRepairModel
    {
        bool IsConfigured { get; }
        string ModelName { get; }

        /// <summary>The method divided into ordered steps, or null when the model gave no usable answer.</summary>
        Task<IReadOnlyList<string>?> SplitStepsAsync(string method, CancellationToken cancellationToken = default);

        /// <summary>For each ingredient line (by index), the quoted phrase stating its amount; lines without one are absent.</summary>
        Task<IReadOnlyDictionary<int, string>> QuoteQuantitiesAsync(IReadOnlyList<string> ingredientLines, string method, CancellationToken cancellationToken = default);
    }
}

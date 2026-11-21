using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nom.Orch.Models.Curation;

namespace Nom.Orch.Interfaces
{
    /// <summary>
    /// The publication year and era of each book source (recipe.RecipeSourceEra), which sets the
    /// container size an unsized can in that book is weighed at. The years are read from the books'
    /// own title pages by ops/source-era.py; nothing is fetched. Every write is journaled
    /// (SourceEraSet) under its run's stamp and undone by it. See
    /// docs/architecture/food-catalog-ingestion.md, "Container sizes".
    /// </summary>
    public interface IRecipeSourceEraService
    {
        /// <summary>Every book source (an attribution on a live recipe with no source site), with its stored year and era, unknown when it has none.</summary>
        Task<RecipeSourceEraListModel> ListAsync(CancellationToken cancellationToken = default);

        /// <summary>Writes the given years: a new source is added, a changed year replaces the stored one, and a source no live book recipe carries or a year outside 1600 to this year is refused. A dry run unless <paramref name="apply"/>; every write is journaled under the returned stamp.</summary>
        Task<RecipeSourceEraWriteReport> SetAsync(IReadOnlyList<RecipeSourceEraInput> rows, bool apply, long personId, CancellationToken cancellationToken = default);

        /// <summary>Takes back every write of the run stamped <paramref name="stamp"/>, newest first; a row changed since is skipped. A dry run unless <paramref name="apply"/>.</summary>
        Task<RecipeSourceEraUndoReport> UndoAsync(DateTime stamp, bool apply, long personId, CancellationToken cancellationToken = default);
    }
}

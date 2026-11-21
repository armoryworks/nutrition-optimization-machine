using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nom.Orch.Models.Curation;

namespace Nom.Orch.Interfaces
{
    /// <summary>
    /// The branded lift: the catalog admission rule's branded branch applied to the evidence a
    /// branded food has (household labels with a gram weight, manufacturer pages from the lookup
    /// lane, Open Food Facts as a signal). It stores how many sources agree and disagree, curates a
    /// pending branded food once two or more agree, never changes a nutrient value and never demotes
    /// a curated food. Every run is journaled under its own stamp and undone by it.
    /// See docs/architecture/food-catalog-ingestion.md, "Branded lift".
    /// </summary>
    public interface IBrandedLiftService
    {
        /// <summary>
        /// Judges the branded foods that have evidence or a stored count (only <paramref name="foodIds"/>
        /// when given). A dry run unless <paramref name="apply"/>; it stops, writing nothing, when it
        /// would change more than <c>MaxChangedFoods</c> foods.
        /// </summary>
        Task<BrandedLiftReport> RunAsync(bool apply, long personId, IReadOnlyCollection<long>? foodIds = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Puts back every value the run stamped <paramref name="stamp"/> wrote, where the food still
        /// holds it; a value changed since is skipped. The undo is journaled under its own stamp, so
        /// undoing that stamp re-applies the run. A dry run unless <paramref name="apply"/>.
        /// </summary>
        Task<BrandedLiftUndoReport> UndoAsync(DateTime stamp, bool apply, long personId, CancellationToken cancellationToken = default);
    }
}

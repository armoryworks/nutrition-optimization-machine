using System;
using System.Threading;
using System.Threading.Tasks;
using Nom.Orch.Models.Curation;

namespace Nom.Orch.Interfaces
{
    /// <summary>
    /// Reads how much of each alcohol line's alcohol stays after the recipe's method (alcohol design
    /// A7) and stores it on the line, so the recipe's label counts only that, journaled and undone by
    /// its time. Rules and the USDA retention table only; no model.
    /// </summary>
    public interface IAlcoholRetentionService
    {
        /// <summary>
        /// Reads every live line on a food that stores alcohol, in recipes after <paramref name="afterRecipeId"/>
        /// (at most <paramref name="maxRecipes"/>), and writes the factor and basis where they change, with an
        /// AlcoholRetention journal entry each, then recalculates those recipes; a dry run unless <paramref name="apply"/>.
        /// An apply commits each batch of recipes on its own; with <paramref name="resume"/> it goes on after the last
        /// committed batch of the latest run that did not finish, under that run's stamp.
        /// </summary>
        Task<AlcoholRetentionRunResult> RunAsync(bool apply, int maxRecipes = 20000, long afterRecipeId = 0, int sampleSize = 50, bool resume = false, CancellationToken cancellationToken = default);

        /// <summary>Gives back the retention each line had before a run at or after <paramref name="sinceUtc"/> and recalculates its recipe; a dry run unless <paramref name="apply"/>.</summary>
        Task<AlcoholRetentionUndoResult> UndoAsync(DateTime sinceUtc, bool apply = false, CancellationToken cancellationToken = default);
    }
}

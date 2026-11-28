using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nom.Orch.Models.Plan;

namespace Nom.Orch.Interfaces
{
    /// <summary>
    /// The judged holds of the stored diet gate (plan.RecipeDietJudgedHold): recipes people judged to hold a food a
    /// restriction type keeps out while the rules showed them. The gate flags each for every set of its type as "Judged to
    /// contain"; a hold never clears a recipe. Rows are loaded by ops/diet-judged-holds.py from the judged files of the diet
    /// draws and staging checkpoints, every write journaled (DietJudgedHoldLoad) under its load's stamp and undone by it.
    /// See docs/architecture/diet-gate.md, "Judged holds".
    /// </summary>
    public interface IDietJudgedHoldService
    {
        /// <summary>Every stored judged hold, newest load first.</summary>
        Task<DietJudgedHoldListModel> ListAsync(CancellationToken cancellationToken = default);

        /// <summary>Loads the given holds: a new recipe and type is added, one already held is left as it is, and a recipe no live recipe is, a type that is no restriction type, a missing food, judge, date or evidence, or a row given twice is refused. A dry run unless <paramref name="apply"/>; every write is journaled under the returned stamp.</summary>
        Task<DietJudgedHoldLoadReport> LoadAsync(IReadOnlyList<DietJudgedHoldInput> rows, bool apply, long personId, CancellationToken cancellationToken = default);

        /// <summary>Takes back every hold the load stamped <paramref name="stamp"/> added; a row changed since is skipped. A dry run unless <paramref name="apply"/>.</summary>
        Task<DietJudgedHoldUndoReport> UndoAsync(DateTime stamp, bool apply, long personId, CancellationToken cancellationToken = default);
    }
}

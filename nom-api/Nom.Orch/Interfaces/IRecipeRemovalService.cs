using System;
using System.Threading;
using System.Threading.Tasks;
using Nom.Orch.Models.Recipe;

namespace Nom.Orch.Interfaces
{
    /// <summary>
    /// The removal of recipes already taken from sources whose effective rights class is forbidden
    /// (docs/architecture/source-rights.md). Stage 1, retire: the recipe leaves every surface and no lane works on it again,
    /// journaled, batched, resumable and undone by its run's stamp. Stage 2, purge: the stored source wording and copies of
    /// retired recipes are deleted for good, after a backup. Every stage has a dry run that equals its apply. A source with a
    /// written permission, a lifted class or a permission-needed class is never touched.
    /// </summary>
    public interface IRecipeRemovalService
    {
        /// <summary>Retires the recipes of one forbidden source, or of every forbidden source when <paramref name="sourceId"/> is null; a dry run unless apply.</summary>
        Task<RecipeRemovalReportModel> RetireAsync(long? sourceId, bool apply, long personId, string? reason, CancellationToken cancellationToken = default);

        /// <summary>Puts back the recipes a retire run took off, by the run's stamp, except those purged since; a dry run unless apply.</summary>
        Task<RecipeRemovalReportModel> UndoRetireAsync(DateTime stamp, bool apply, long personId, CancellationToken cancellationToken = default);

        /// <summary>Deletes for good the stored source wording and copies of retired recipes (one source's, or all); irreversible, so the apply needs <paramref name="backupTaken"/>; a dry run unless apply.</summary>
        Task<RecipeRemovalReportModel> PurgeAsync(long? sourceId, bool apply, bool backupTaken, long personId, CancellationToken cancellationToken = default);

        /// <summary>Per source the recipes taken, retired and purged, the runs and an unfinished run.</summary>
        Task<RecipeRemovalStatusModel> StatusAsync(CancellationToken cancellationToken = default);
    }
}

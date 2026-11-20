using System;
using System.Threading;
using System.Threading.Tasks;
using Nom.Orch.Models.Curation;

namespace Nom.Orch.Interfaces
{
    /// <summary>The undo of the clean-room prose lane's rewrites since a time, optionally of one lane version only.</summary>
    public interface IProseRewriteUndoService
    {
        /// <summary>Restores the text, steps and prose state each rewrite since <paramref name="sinceUtc"/> replaced; a dry run unless <paramref name="apply"/>.</summary>
        Task<ProseRewriteUndoResult> UndoAsync(DateTime sinceUtc, string? laneVersion = null, bool apply = false, CancellationToken cancellationToken = default);
    }
}

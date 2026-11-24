using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nom.Orch.Models.Recipe;

namespace Nom.Orch.Interfaces
{
    /// <summary>Reads the queue of lines the line guard held (recipe.LineGuardHold) for the operator, the line judge and later rules.</summary>
    public interface ILineGuardHoldService
    {
        /// <summary>The open holds by reason, in all and per writer, and the resolved ones by resolution.</summary>
        Task<LineGuardHoldReport> CountsAsync(CancellationToken cancellationToken = default);

        /// <summary>Holds after <paramref name="afterId"/> in id order, open ones unless <paramref name="resolved"/>, of one reason or writer when given.</summary>
        Task<List<LineGuardHoldRow>> ListAsync(string? reason, string? origin, bool resolved, long afterId, int take, CancellationToken cancellationToken = default);
    }
}

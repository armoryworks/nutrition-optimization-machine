using System;
using System.Threading.Tasks;
using Nom.Orch.Models.Curation;

namespace Nom.Orch.Interfaces
{
    /// <summary>
    /// Admin repair of scraped recipe lines counted in pieces, cans or packages whose own wording
    /// names a can or package: a line that states a size ("1 (15 oz) can black beans") takes that
    /// size, and a jar, bottle, box or bag counted as a piece becomes a package. Deterministic
    /// reading only, every change is written to the audit log, and a change can be undone from it.
    /// </summary>
    public interface IPackageLineService
    {
        Task<PackageLinePreview> PreviewAsync(int sampleSize = 50);

        /// <summary>
        /// Changes up to <paramref name="maxLines"/> lines, and marks the can and package lines it
        /// leaves alone as modified when they were weighed as one item or one serving, so their
        /// recipe's label is recalculated without that weight.
        /// </summary>
        Task<PackageLineResult> ApplyAsync(int maxLines = 5000);

        /// <summary>
        /// Puts back the quantity and measurement of every line changed at or after
        /// <paramref name="sinceUtc"/> that still holds what the change gave it.
        /// </summary>
        Task<PackageLineUndoResult> UndoAsync(DateTime sinceUtc);
    }
}

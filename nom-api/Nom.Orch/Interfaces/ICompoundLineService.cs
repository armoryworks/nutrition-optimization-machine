using System.Collections.Generic;
using System.Threading.Tasks;
using Nom.Orch.Models.Curation;

namespace Nom.Orch.Interfaces
{
    /// <summary>
    /// Admin repair of scraped recipe lines that name more than one food ("salt and pepper to
    /// taste", "1 cup each flour and sugar", "butter or margarine"). Deterministic rules only
    /// (<see cref="Nom.Orch.Services.Support.CompoundLineSplitter"/>), rolled out rule by rule;
    /// every line changed is written to the audit log and put back by the relink undo.
    /// </summary>
    public interface ICompoundLineService
    {
        /// <summary>
        /// What applying the given rules (every rule when none are given) would do: lines, row
        /// moves and rows added per rule, the compound lines left alone by reason, and samples; a
        /// line whose move the diet guard would hold is counted apart (HeldByDiet), as the apply
        /// counts it. Writes nothing.
        /// </summary>
        Task<CompoundLinePreview> PreviewAsync(IReadOnlyCollection<string>? rules = null, int sampleSize = 50);

        /// <summary>
        /// Applies the given rules to up to <paramref name="maxLines"/> lines. A split line keeps
        /// its row for the part on its food (or the first part) and gains one row per other food at
        /// its own position, or at the end of the recipe when that food already holds it; a line
        /// with alternatives moves to its first alternative with its text and amount unchanged.
        /// </summary>
        Task<CompoundLineResult> ApplyAsync(IReadOnlyCollection<string> rules, int maxLines = 5000);
    }
}

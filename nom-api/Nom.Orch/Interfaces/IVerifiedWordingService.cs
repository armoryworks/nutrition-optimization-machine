using System.Collections.Generic;
using System.Threading.Tasks;
using Nom.Orch.Models.Curation;

namespace Nom.Orch.Interfaces
{
    /// <summary>
    /// Admin loading of the reviewed wording table (db/verified-wordings.tsv) into
    /// recipe.IngredientWording. Every row written is journaled, and a batch is taken out as one.
    /// </summary>
    public interface IVerifiedWordingService
    {
        /// <summary>
        /// Loads one reviewed batch. A row is refused with a reason when its wording is not in key
        /// form, its verdict is unknown, its food is missing, branded, without nutrition or not
        /// curated, its wording is the name of a curated food, another row gives the wording
        /// something else, or the table already gives it something else and
        /// <paramref name="replace"/> is not set. A USDA Foundation or SR Legacy food awaiting
        /// curation is curated by the load. Without <paramref name="apply"/> nothing is written
        /// and the result is what the same call would do.
        /// </summary>
        Task<VerifiedWordingLoadResult> LoadAsync(IReadOnlyCollection<VerifiedWordingRow> rows, string batch, bool apply = false, bool replace = false);

        /// <summary>
        /// Takes every row of a batch out of the table, except that a row the batch took over from
        /// an earlier batch with replace goes back to that batch's verdict, food and note, and
        /// sends back to awaiting curation each food a load curated that no wording points at any
        /// more. Without <paramref name="apply"/> nothing is written.
        /// </summary>
        Task<VerifiedWordingUnloadResult> UnloadAsync(string batch, bool apply = false);

        /// <summary>The live table as TSV, for diffing against db/verified-wordings.tsv.</summary>
        Task<string> ExportTsvAsync();
    }
}

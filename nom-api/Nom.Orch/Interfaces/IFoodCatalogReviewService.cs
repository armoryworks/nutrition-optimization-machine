using System.Collections.Generic;
using System.Threading.Tasks;
using Nom.Orch.Models.Curation;

namespace Nom.Orch.Interfaces
{
    /// <summary>
    /// Admin review of the imported food catalog, and the proposal pipeline that automated
    /// reviewers feed. Proposals are never applied automatically — an admin approves them.
    /// </summary>
    public interface IFoodCatalogReviewService
    {
        /// <param name="source">FdcDataType filter ("foundation_food", "branded_food", "authored").</param>
        /// <param name="status">Curation status id filter (e.g. pending).</param>
        /// <param name="foodGroupId">Food-group filter; use 0 for "unclassified".</param>
        /// <param name="search">Name contains.</param>
        Task<FoodCatalogPageModel> GetPageAsync(
            string? source, long? status, long? foodGroupId, string? search, int page, int pageSize);

        Task<FoodCatalogItemModel?> UpdateAsync(long id, FoodCatalogUpdateModel model);

        /// <summary>Bulk promote reviewed rows to Curated so meal planning can use them; rows already in that status are left untouched and counted separately.</summary>
        Task<(int Updated, int Unchanged)> SetCurationStatusAsync(IEnumerable<long> ingredientIds, long curationStatusId);

        /// <summary>Exports the catalog as CSV for an external reviewer to read.</summary>
        Task<string> ExportCsvAsync(string? source, long? status, long? foodGroupId, string? search, int limit);

        /// <summary>
        /// Ingests a proposal CSV. Rows that violate <see cref="Nom.Data.Nutrition.ProposalPolicy"/>
        /// (e.g. a model reviewer proposing a nutrient value) are rejected with a reason.
        /// </summary>
        Task<FoodProposalIngestResult> IngestProposalsCsvAsync(string csv, string batch);

        Task<List<FoodProposalModel>> GetProposalsAsync(string? batch, string? status, int limit);

        /// <summary>Pending USDA link proposals grouped by the food they point at, most recipe lines first.</summary>
        Task<List<FoodProposalGroupModel>> GetLinkGroupsAsync(string? source, bool? verified, int limit);

        /// <summary>Applies up to <paramref name="max"/> pending link proposals that point at one food; Remaining says how many are left.</summary>
        Task<FoodProposalGroupApplyResult> ApplyLinkGroupAsync(long targetIngredientId, string? source, bool? verified, int max, long reviewerPersonId);

        /// <summary>Approves a proposal and applies it to the catalog.</summary>
        Task<bool> ApplyProposalAsync(long proposalId, long reviewerPersonId);

        Task<bool> RejectProposalAsync(long proposalId, long reviewerPersonId);

        /// <summary>Applies each proposal in turn; ones that no longer apply are skipped, not failed.</summary>
        Task<FoodProposalBatchResult> ApplyProposalsAsync(IEnumerable<long> proposalIds, long reviewerPersonId);
    }
}

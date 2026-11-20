using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nom.Data.Curation;
using Nom.Orch.Models.Curation;

namespace Nom.Orch.Interfaces
{
    /// <summary>A catalog ingredient that recipes use but that isn't linked to a USDA food yet.</summary>
    public sealed record LinkSource(long IngredientId, string Name, int Uses);

    /// <summary>
    /// What a proposal pass did. Unchanged counts model rejections due again whose question (model,
    /// prompt version, wording and shortlist) had not changed, so the model was not asked; Unreadable
    /// counts ingredients the model left unanswered, which get no row and are asked again.
    /// </summary>
    public sealed record IngredientLinkBatchResult(int Seen, int ExactProposals, int AiProposals, int NoMatch, int Unchanged = 0, int Unreadable = 0);

    /// <summary>The latest link proposal for a wording, as the link-impact list shows it.</summary>
    public sealed record LinkImpactProposal(long Id, FoodProposalStatus Status, string Source, string? FdcId, long? TargetIngredientId, string? TargetName, decimal? Confidence, string? Verification);

    /// <summary>
    /// An uncurated wording that is the only thing keeping recipes from the auto-approve sweep: how
    /// many recipes and lines it alone blocks, and the latest link proposal for it, if any.
    /// </summary>
    public sealed record LinkImpactRow(long IngredientId, string Wording, int RecipesUnlocked, int Lines, LinkImpactProposal? Proposal);

    /// <summary>How many pending model picks were checked in a pass, how many of those all three checks verified, and whether more await checking.</summary>
    public sealed record IngredientLinkVerifyResult(int Checked, int Verified, bool MoreWaiting);

    /// <summary>
    /// Proposes links from catalog ingredients (mostly names minted by recipe imports) to USDA
    /// foods, most-used first. Unambiguous name matches are proposed deterministically; the rest
    /// go to the local model to choose among shortlisted USDA rows. Every link is an admin-reviewed
    /// FoodCatalogProposal (field "fdc_link"); nothing changes until one is approved.
    /// </summary>
    public interface IIngredientLinkService
    {
        Task<IReadOnlyList<LinkSource>> NextSourcesAsync(int count, CancellationToken cancellationToken = default);

        /// <summary>
        /// The uncurated ingredients <see cref="NextSourcesAsync"/> draws from, before it drops the
        /// unused ones and orders by use, with a machine rejection made before
        /// <paramref name="retryBefore"/> counted as due again.
        /// </summary>
        IQueryable<Nom.Data.Recipe.IngredientEntity> WaitingSources(DateTime retryBefore);

        /// <summary>Null when the model is needed but unreachable; those sources stay unproposed.</summary>
        Task<IngredientLinkBatchResult?> ProposeAsync(IReadOnlyList<LinkSource> sources, CancellationToken cancellationToken = default);

        /// <summary>Re-points pending links for bare staple names to their standard USDA entry and withdraws name matches the matcher no longer makes; returns how many changed.</summary>
        Task<int> ApplyStapleDefaultsToPendingAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Pending links that are certain — staples and same-name USDA attaches — which are applied
        /// without an admin, most-used first. Name-matcher and model picks stay with reviewers.
        /// </summary>
        Task<IReadOnlyList<long>> PendingDeterministicAsync(int count, IReadOnlyCollection<long> skip, bool includeNameMatches = false, CancellationToken cancellationToken = default);

        /// <summary>
        /// Triangulates pending model picks, those that would release the most recipes first, then
        /// most-used: a pick is verified only when the model was at least
        /// <paramref name="minConfidence"/> sure, the name matcher ranks the same USDA food first, and
        /// a judge on a different model says the wording and the USDA description are the same food.
        /// The outcome, with the judge's model and prompt version, is recorded on the proposal's
        /// reason, so a pick is checked once; nothing is applied here. A pick the judge's own model
        /// made is never judged, and without a judge model nothing is checked. Picks below the
        /// confidence bar are left unmarked and unasked. Null when the judge is unreachable; the
        /// picks it would have checked stay unchecked.
        /// </summary>
        Task<IngredientLinkVerifyResult?> VerifyPendingAiAsync(int count, decimal minConfidence, CancellationToken cancellationToken = default);

        /// <summary>
        /// The uncurated wordings that alone block the most recipes from the auto-approve sweep: recipes
        /// that pass its status, licence, image and vetting rules and whose every uncurated, quantified,
        /// non-exempt line is on that one wording. Upper bounds, since the sweep's own policy still
        /// applies. Read-only; cached for a few minutes.
        /// </summary>
        Task<IReadOnlyList<LinkImpactRow>> TopBlockersAsync(int limit, CancellationToken cancellationToken = default);

        /// <summary>Pending model picks that triangulation verified, most-used first, for applying without an admin.</summary>
        Task<IReadOnlyList<long>> PendingVerifiedAsync(int count, IReadOnlyCollection<long> skip, decimal minConfidence = 0m, CancellationToken cancellationToken = default, IReadOnlyCollection<string>? excludeTargets = null);

        /// <summary>
        /// How many more model picks the lane may apply on its own in the rolling day, and the USDA
        /// foods that have already taken their share of them.
        /// </summary>
        Task<LinkApplyAllowance> VerifiedAllowanceAsync(int dailyCap, int perTargetCap, CancellationToken cancellationToken = default);

        /// <summary>
        /// Rechecks every live alias that has no context against today's deterministic rules and
        /// marks the ones a rule gives to the food they are on ("deterministic:recheck:name-key",
        /// ":staple" or ":name-match"), so imports may follow them. An alias a rule gives to another
        /// food, or that no rule covers, stays without a context. Without <paramref name="apply"/>
        /// nothing is written. Each mark is written to the audit log and can be undone from it.
        /// </summary>
        Task<AliasClassifyResult> ClassifyAliasesAsync(bool apply, long personId, int sampleSize = 50, CancellationToken cancellationToken = default);

        /// <summary>
        /// Takes the context off every alias marked at or after <paramref name="sinceUtc"/> that
        /// still carries the mark; one whose context has changed since is skipped.
        /// </summary>
        Task<AliasClassifyUndoResult> UndoAliasClassificationAsync(DateTime sinceUtc, long personId, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// What is left of the lane's daily allowance, the USDA foods (by FDC id) it must leave alone
    /// until tomorrow, and how many links each USDA food has already taken in the rolling day.
    /// </summary>
    public sealed record LinkApplyAllowance(int Remaining, IReadOnlyCollection<string> BusyTargets, IReadOnlyDictionary<string, int>? AppliedByTarget = null);
}

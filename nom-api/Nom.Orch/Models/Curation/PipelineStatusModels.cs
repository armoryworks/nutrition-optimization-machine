using System;
using System.Collections.Generic;

namespace Nom.Orch.Models.Curation
{
    /// <summary>Where recipes, recipe lines and foods are waiting in the automated catalog pipeline, as of one moment.</summary>
    public sealed record PipelineStatusModel
    {
        public DateTime GeneratedAt { get; init; }
        public long ComputeMs { get; init; }
        /// <summary>True when this copy was served from the cache rather than computed for this request.</summary>
        public bool FromCache { get; init; }
        public PipelineRecipeCounts Recipes { get; init; } = new();
        public PipelineLines Lines { get; init; } = new();
        public PipelineFoodCounts Foods { get; init; } = new();
        public PipelineStuck Stuck { get; init; } = new();
        public PipelineProposalCounts Proposals { get; init; } = new();
        public PipelineImageCounts Images { get; init; } = new();
        public IReadOnlyList<PipelineLane> Lanes { get; init; } = Array.Empty<PipelineLane>();
        public PipelineAiStatus Ai { get; init; } = new();
        public IReadOnlyList<PipelineJournalRow> Journal { get; init; } = Array.Empty<PipelineJournalRow>();
    }

    /// <summary>Live recipes by curation state, and what holds back the ones waiting.</summary>
    public sealed record PipelineRecipeCounts
    {
        public int Approved { get; init; }
        public int Waiting { get; init; }
        public int NeedsRevision { get; init; }
        public int PendingReview { get; init; }
        public int Rejected { get; init; }
        /// <summary>Waiting recipes by the number of distinct uncurated foods on their ingredient lines.</summary>
        public PipelineBlockedBy WaitingBlockedBy { get; init; } = new();
        public int WaitingPublicDomain { get; init; }
        /// <summary>Waiting scraped recipes of unknown licence that have no image of their own.</summary>
        public int WaitingScrapedWithoutOwnImage { get; init; }
        public int WaitingWithSourceProse { get; init; }
        /// <summary>Recipes whose own prose is withheld (an earlier paraphrase of its source, or a source's rights) until the rewrite lane replaces it; the goal is none.</summary>
        public int ProseWithheld { get; init; }
        /// <summary>Of those withheld, the approved ones, which the rewrite lane takes first.</summary>
        public int ProseWithheldApproved { get; init; }
        /// <summary>Of those withheld, the recipes in whose old text the prose check reads copied sentences (<c>ProseCopiedSentences</c>, the queue figure): the rewrite lane takes them first, most copied first.</summary>
        public int ProseRepair { get; init; }
        /// <summary>Of those with copied sentences, the approved ones.</summary>
        public int ProseRepairApproved { get; init; }
        /// <summary>Recipes whose own prose the prose check is unsure of on facts (weak signals and fact classes not proven): shown, marked and counted, and the prose judge's to read.</summary>
        public int ProseUnsure { get; init; }
        /// <summary>Of those unsure, the approved ones.</summary>
        public int ProseUnsureApproved { get; init; }
        /// <summary>Withheld or quarantined recipes the clean-room rewrite lane gave up on under its current version; each is a failure of the engine to fix, and they are tried again when the lane version changes.</summary>
        public int ProseGaveUp { get; init; }
        /// <summary>The lane's give-ups under its current version, by the failure class that held each one.</summary>
        public IReadOnlyDictionary<string, int> ProseGaveUpByClass { get; init; } = new Dictionary<string, int>();
        public int RevisionMissingQuantities { get; init; }
        public int RevisionSteps { get; init; }
        /// <summary>Approved recipes holding a measured ingredient line their label leaves out.</summary>
        public int ApprovedWithUncountedLine { get; init; }
        public int ApprovedWithoutImage { get; init; }
        /// <summary>Recipes that use a food with nutrients and have no nutrition label.</summary>
        public int WithoutNutritionLabel { get; init; }
        public DateTime? LastAutoApprovedAt { get; init; }
        public int AutoApprovedLast24h { get; init; }
        public DateTime? LastScrapedAt { get; init; }
        public int CreatedLast7d { get; init; }
    }

    public sealed record PipelineBlockedBy
    {
        public int None { get; init; }
        public int One { get; init; }
        public int Two { get; init; }
        public int ThreeOrMore { get; init; }
    }

    public sealed record PipelineLines
    {
        public PipelineLineFunnel All { get; init; } = new();
        public PipelineLineFunnel Approved { get; init; } = new();
        /// <summary>Ingredient lines whose text contains " + "; an upper bound, since a source line can itself say "1 cup + 2 tbsp".</summary>
        public int Folded { get; init; }
        public int FoldedOnApproved { get; init; }
    }

    /// <summary>
    /// Live recipe lines, each counted once by the label rule of RecipeNutritionService.StatusFor
    /// (notAnIngredient, notMeasured, noData, noConversion, counted), plus the cleanup series'
    /// own counts on curated foods.
    /// </summary>
    public sealed record PipelineLineFunnel
    {
        public int Total { get; init; }
        public int NotAnIngredient { get; init; }
        public int Ingredient { get; init; }
        public int OnCuratedFood { get; init; }
        public int Unresolved { get; init; }
        public int NotMeasured { get; init; }
        public int NoData { get; init; }
        public int NoConversion { get; init; }
        public int Counted { get; init; }
        /// <summary>Counted lines in pieces whose food has no item weight, weighed by its reference serving instead.</summary>
        public int CountedByServingFallback { get; init; }
        /// <summary>Measured ingredient lines in pieces on a curated food with no item weight (metrics.sql).</summary>
        public int CountWithoutItemWeight { get; init; }
        /// <summary>Measured ingredient lines by volume on a curated food with no density (metrics.sql).</summary>
        public int VolumeWithoutDensity { get; init; }
        /// <summary>Measured ingredient lines on a curated food with a gram conversion and no serving fallback (metrics.sql "countable").</summary>
        public int CountableStrict { get; init; }
    }

    public sealed record PipelineFoodCounts
    {
        public int Curated { get; init; }
        public int PendingReview { get; init; }
        public int NotCurated { get; init; }
        public int CuratedWithoutNutrition { get; init; }
        public int CuratedWithoutUsdaLink { get; init; }
        public int CuratedWithoutFoodGroup { get; init; }
        /// <summary>Curated foods with measured piece lines and no item weight.</summary>
        public int FoodsMissingItemWeight { get; init; }
        /// <summary>Curated foods with measured volume lines and no density.</summary>
        public int FoodsMissingDensity { get; init; }
        /// <summary>Distinct uncurated foods on any live line; curated foods are not counted.</summary>
        public int WordingsInUse { get; init; }
        /// <summary>Distinct uncurated foods on ingredient lines only.</summary>
        public int UnresolvedWordings { get; init; }
        /// <summary>Unresolved wordings by how many ingredient lines use each.</summary>
        public IReadOnlyList<PipelineWordingBucket> WordingsByUse { get; init; } = Array.Empty<PipelineWordingBucket>();
        /// <summary>Wordings in use by the state of their USDA link proposals.</summary>
        public IReadOnlyList<PipelineLinkStateCount> UnresolvedByLinkState { get; init; } = Array.Empty<PipelineLinkStateCount>();
        /// <summary>Pending foods the catalog admission rule holds with a stored reason other than waiting for a second source; work for no person.</summary>
        public int HeldByRule { get; init; }
        /// <summary>The held foods by reason, a duplicate's or category's id left out; a food held for two reasons counts under both.</summary>
        public IReadOnlyList<PipelineReasonCount> HeldByReason { get; init; } = Array.Empty<PipelineReasonCount>();
        /// <summary>Pending branded foods whose only reason is that no second independent source agrees yet.</summary>
        public int AwaitingSecondSource { get; init; }
        /// <summary>Pending foods with no stored admission reason: made by hand or never judged by the rule.</summary>
        public int PendingWithoutReason { get; init; }
        /// <summary>Foods an admin rejected; the admission rule rejects none.</summary>
        public int Rejected { get; init; }
        public PipelineBrandedCounts Branded { get; init; } = new();
        public PipelinePreparedContents PreparedContents { get; init; } = new();
    }

    /// <summary>
    /// Curated prepared meals by what is known of their contents (the diet gate offers one to a household judged by
    /// contents only when a statement was read and every ingredient resolved or recognised): read with every
    /// ingredient known, read with an ingredient not resolved, statement not readable, and no statement at all; with
    /// how many have an FDC statement and how many a household's confirmed label. A meal counts by its current FDC
    /// statement, the one every household may rely on; a household's label serves only that household and is counted apart.
    /// </summary>
    public sealed record PipelinePreparedContents
    {
        public int Meals { get; init; }
        public int Read { get; init; }
        public int ReadUnresolved { get; init; }
        public int Unread { get; init; }
        public int None { get; init; }
        public int FromFdc { get; init; }
        public int FromLabels { get; init; }
    }

    public sealed record PipelineReasonCount(string Reason, int Foods);

    /// <summary>
    /// Branded foods by the branded lift's count of agreeing sources (the FDC record counting as
    /// one): curated by agreeing sources at two or more with no stored reason, curated by an admin
    /// (fewer than two, or a reason the rule still holds), curated but a later source disagrees, and
    /// pending ones awaiting a second source or held by a disagreement.
    /// </summary>
    public sealed record PipelineBrandedCounts
    {
        public IReadOnlyList<PipelineSourceCount> CuratedByAgreeingSources { get; init; } = Array.Empty<PipelineSourceCount>();
        public int CuratedWithoutCount { get; init; }
        public int CuratedWithDisagreement { get; init; }
        public int Awaiting { get; init; }
        public int HeldByDisagreement { get; init; }
        public int HeldOther { get; init; }
    }

    public sealed record PipelineSourceCount(string Sources, int Foods);

    public sealed record PipelineWordingBucket(string Bucket, int Wordings, int Lines);

    public sealed record PipelineLinkStateCount(string State, int Foods, int Lines);

    /// <summary>The foods holding back the most lines, most lines first.</summary>
    public sealed record PipelineStuck
    {
        public IReadOnlyList<PipelineStuckFood> UnresolvedWordings { get; init; } = Array.Empty<PipelineStuckFood>();
        public IReadOnlyList<PipelineStuckFood> CountWithoutItemWeight { get; init; } = Array.Empty<PipelineStuckFood>();
        public IReadOnlyList<PipelineStuckFood> VolumeWithoutDensity { get; init; } = Array.Empty<PipelineStuckFood>();
    }

    public sealed record PipelineStuckFood(long IngredientId, string Name, int Lines);

    public sealed record PipelineProposalCounts
    {
        public int Pending { get; init; }
        public int PendingModelPicks { get; init; }
        /// <summary>Pending model picks the triangulation marked verified.</summary>
        public int PendingModelPicksVerified { get; init; }
        public int PendingExactMatches { get; init; }
        public int PendingOther { get; init; }
        /// <summary>Uncurated foods in use that the link lane would ask about next.</summary>
        public int LinkQueue { get; init; }
        public int VerifiedAppliedLast24h { get; init; }
        public int VerifiedDailyCap { get; init; }
        public int VerifiedPerFoodCap { get; init; }
        public int FoodsAtCap { get; init; }
        public int NameMatchAppliedLast24h { get; init; }
        public int NameMatchDailyCap { get; init; }
        public int NameMatchPerFoodCap { get; init; }
        public int NameMatchFoodsAtCap { get; init; }
        public DateTime? LastProposedAt { get; init; }
        public DateTime? LastAppliedBySystemAt { get; init; }
    }

    public sealed record PipelineImageCounts
    {
        public int ApprovedWithoutImage { get; init; }
        /// <summary>Approved recipes without an image that the image finder has never searched for.</summary>
        public int NeverSearched { get; init; }
        public DateTime? LastSearchedAt { get; init; }
        public PipelineImageCandidateCounts Candidates { get; init; } = new();
    }

    public sealed record PipelineImageCandidateCounts
    {
        public int Pending { get; init; }
        public int Attached { get; init; }
        public int Approved { get; init; }
        public int Rejected { get; init; }
        public int Superseded { get; init; }
        public int Removed { get; init; }
    }

    /// <summary>
    /// One lane of the pipeline as its settings and its data show it. LastActivityAt is the last
    /// change seen in the data, not a heartbeat; a model lane's live state comes from the lane gate,
    /// matched by <see cref="GateLane"/>.
    /// </summary>
    public sealed record PipelineLane
    {
        public string Key { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        /// <summary>The lane gate's name for this lane (Nom.Orch.Services.Support.AiLanes); null for a lane without a model.</summary>
        public string? GateLane { get; init; }
        /// <summary>Whether the lane runs outside the API (the harvest timer, the image finder).</summary>
        public bool External { get; init; }
        public bool Enabled { get; init; }
        public bool UsesModel { get; init; }
        public bool? ModelConfigured { get; init; }
        public string? Model { get; init; }
        public IReadOnlyList<PipelineLaneSwitch> Switches { get; init; } = Array.Empty<PipelineLaneSwitch>();
        public IReadOnlyList<PipelineLaneCap> Caps { get; init; } = Array.Empty<PipelineLaneCap>();
        public int? Queue { get; init; }
        public string? QueueLabel { get; init; }
        public DateTime? LastActivityAt { get; init; }
        public int? ActivityLast24h { get; init; }
    }

    /// <summary>A start-up setting of a lane, by its configuration key under the lane's section.</summary>
    public sealed record PipelineLaneSwitch(string Key, string Label, bool On);

    /// <summary>A limit of a lane; Used is null when the page has no count for it.</summary>
    public sealed record PipelineLaneCap(string Label, int Limit, int? Used);

    public sealed record PipelineAiStatus
    {
        public string QuietHours { get; init; } = string.Empty;
        public string QuietTimeZone { get; init; } = string.Empty;
        /// <summary>Minutes of quiet hours left now, or null outside them; set per request.</summary>
        public int? QuietMinutesLeft { get; init; }
    }

    public sealed record PipelineJournalRow(string EntityType, string ChangeType, int Last24h, int Last7d, int Total, DateTime? LastAt);
}

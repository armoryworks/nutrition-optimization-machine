using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Nom.Data;
using Nom.Data.Curation;
using Nom.Data.Measurement;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Curation;
using Nom.Orch.Services.Support;

namespace Nom.Orch.Services
{
    /// <summary>
    /// Counts for the admin pipeline page. Every number is a query the lanes or the cleanup series
    /// already use, run one after another on one context and cached as an immutable model for
    /// <see cref="CacheMinutes"/>; one computation runs at a time however many admins ask.
    /// </summary>
    public class PipelineStatusService : IPipelineStatusService
    {
        public const int CacheMinutes = 10;
        public const int TopCount = 10;
        private const string CacheKey = nameof(PipelineStatusService);
        private const string AutoApprovedNote = "auto-approved:";
        private const long Curated = (long)CurationStatusEnum.Curated;
        private const long NonCurated = (long)CurationStatusEnum.NonCurated;
        private const long PendingCuration = (long)CurationStatusEnum.PendingCuration;
        private const long RequiresRevision = (long)CurationStatusEnum.RequiresRevision;
        private const long Rejected = (long)CurationStatusEnum.Rejected;
        private const string IngredientKind = RecipeIngredientLineKind.Ingredient;
        private static readonly SemaphoreSlim Gate = new(1, 1);

        private readonly ApplicationDbContext _db;
        private readonly IMemoryCache _cache;
        private readonly IConfiguration _configuration;
        private readonly IIngredientLinkService _links;
        private readonly ILinkAutoApplyService _autoApply;
        private readonly IIngredientLinkMatcher _matcher;
        private readonly IRecipeRepairModel _repairModel;
        private readonly IRecipeClassificationModel _classificationModel;
        private readonly IRecipeToolTagger _toolTagger;

        public PipelineStatusService(
            ApplicationDbContext db,
            IMemoryCache cache,
            IConfiguration configuration,
            IIngredientLinkService links,
            ILinkAutoApplyService autoApply,
            IIngredientLinkMatcher matcher,
            IRecipeRepairModel repairModel,
            IRecipeClassificationModel classificationModel,
            IRecipeToolTagger toolTagger)
        {
            _db = db;
            _cache = cache;
            _configuration = configuration;
            _links = links;
            _autoApply = autoApply;
            _matcher = matcher;
            _repairModel = repairModel;
            _classificationModel = classificationModel;
            _toolTagger = toolTagger;
        }

        public async Task<PipelineStatusModel> GetAsync(bool refresh = false, CancellationToken cancellationToken = default)
        {
            if (!refresh && _cache.TryGetValue<PipelineStatusModel>(CacheKey, out var cached) && cached != null)
                return cached with { FromCache = true };

            await Gate.WaitAsync(cancellationToken);
            try
            {
                if (!refresh && _cache.TryGetValue(CacheKey, out cached) && cached != null)
                    return cached with { FromCache = true };

                var status = await ComputeAsync(cancellationToken);
                _cache.Set(CacheKey, status, TimeSpan.FromMinutes(CacheMinutes));
                return status;
            }
            finally
            {
                Gate.Release();
            }
        }

        private async Task<PipelineStatusModel> ComputeAsync(CancellationToken cancellationToken)
        {
            var clock = Stopwatch.StartNew();
            var now = DateTime.UtcNow;
            if (_db.Database.IsRelational()) _db.Database.SetCommandTimeout(TimeSpan.FromSeconds(60));

            var recipeRow = await RecipeQuery(_db, now, ProseRetryBefore(now)).FirstOrDefaultAsync(cancellationToken) ?? new RecipeRow();
            var blockedRows = await BlockedByRecipeQuery(_db).ToListAsync(cancellationToken);
            var uncounted = await ApprovedWithUncountedLineQuery(_db).CountAsync(cancellationToken);
            var withoutLabel = await WithoutNutritionLabelQuery(_db).CountAsync(cancellationToken);
            var autoApprovals = await AutoApprovalQuery(_db, now).FirstOrDefaultAsync(cancellationToken);

            var blocked = new PipelineBlockedBy
            {
                None = Math.Max(0, recipeRow.Waiting - blockedRows.Count),
                One = blockedRows.Count(c => c == 1),
                Two = blockedRows.Count(c => c == 2),
                ThreeOrMore = blockedRows.Count(c => c >= 3),
            };
            var recipes = new PipelineRecipeCounts
            {
                Approved = recipeRow.Approved,
                Waiting = recipeRow.Waiting,
                NeedsRevision = recipeRow.NeedsRevision,
                PendingReview = recipeRow.PendingReview,
                Rejected = recipeRow.Rejected,
                WaitingBlockedBy = blocked,
                WaitingPublicDomain = recipeRow.WaitingPublicDomain,
                WaitingScrapedWithoutOwnImage = recipeRow.WaitingScrapedWithoutOwnImage,
                WaitingWithSourceProse = recipeRow.WaitingWithSourceProse,
                RevisionMissingQuantities = recipeRow.RevisionMissingQuantities,
                RevisionSteps = recipeRow.RevisionSteps,
                ApprovedWithUncountedLine = uncounted,
                ApprovedWithoutImage = recipeRow.ApprovedWithoutImage,
                WithoutNutritionLabel = withoutLabel,
                LastAutoApprovedAt = autoApprovals?.Last,
                AutoApprovedLast24h = autoApprovals?.Day ?? 0,
                LastScrapedAt = recipeRow.LastScrapedAt,
                CreatedLast7d = recipeRow.CreatedLast7d,
            };

            var funnelRows = await LineFunnelQuery(_db).ToListAsync(cancellationToken);
            var approvedRow = funnelRows.FirstOrDefault(r => r.Approved) ?? new FunnelRow();
            var allRow = funnelRows.Aggregate(new FunnelRow(), Add);
            var lines = new PipelineLines
            {
                All = ToFunnel(allRow),
                Approved = ToFunnel(approvedRow),
                Folded = allRow.Folded,
                FoldedOnApproved = approvedRow.Folded,
            };

            var (foods, stuck) = await FoodsAndStuckAsync(cancellationToken);
            var (proposals, proposedDay) = await ProposalsAsync(now, cancellationToken);
            var images = await ImagesAsync(recipeRow, cancellationToken);
            var journal = await JournalQuery(_db, now).ToListAsync(cancellationToken);
            journal = journal.OrderByDescending(j => j.LastAt).ThenBy(j => j.EntityType).ThenBy(j => j.ChangeType).ToList();
            var lanes = await LanesAsync(now, recipes, recipeRow, proposals, proposedDay, images, journal, cancellationToken);

            return new PipelineStatusModel
            {
                GeneratedAt = now,
                ComputeMs = clock.ElapsedMilliseconds,
                Recipes = recipes,
                Lines = lines,
                Foods = foods,
                Stuck = stuck,
                Proposals = proposals,
                Images = images,
                Lanes = lanes,
                Ai = new PipelineAiStatus
                {
                    QuietHours = _configuration.GetValue("Ai:QuietHours", "01:00-05:00") ?? string.Empty,
                    QuietTimeZone = _configuration.GetValue("Ai:QuietHoursTimeZone", "America/Denver") ?? string.Empty,
                },
                Journal = journal,
            };
        }

        private DateTime ProseRetryBefore(DateTime now) =>
            now.AddDays(-Math.Max(1, _configuration.GetValue("ProseRewrite:RetryDays", 30)));

        private async Task<(PipelineFoodCounts, PipelineStuck)> FoodsAndStuckAsync(CancellationToken cancellationToken)
        {
            var foodRow = await FoodQuery(_db).FirstOrDefaultAsync(cancellationToken);
            var withoutNutrition = await CuratedWithoutNutritionQuery(_db).CountAsync(cancellationToken);

            var uses = await UnresolvedByFoodQuery(_db).ToListAsync(cancellationToken);
            var unresolved = uses.Where(u => u.IngredientLines > 0).ToList();

            var pending = (await PendingLinkFoodsQuery(_db).ToListAsync(cancellationToken)).ToHashSet();
            var rejected = await RejectedLinkFoodsQuery(_db).ToListAsync(cancellationToken);
            var noCandidate = rejected.Where(r => r.NoCandidate).Select(r => r.IngredientId).ToHashSet();
            var rejectedPick = rejected.Where(r => !r.NoCandidate).Select(r => r.IngredientId).ToHashSet();

            var states = uses
                .GroupBy(u => pending.Contains(u.IngredientId) ? "pendingPick"
                    : noCandidate.Contains(u.IngredientId) ? "noCandidate"
                    : rejectedPick.Contains(u.IngredientId) ? "rejectedPick"
                    : "neverProposed")
                .ToDictionary(g => g.Key, g => (Foods: g.Count(), Lines: g.Sum(u => u.Lines)));
            var linkStates = new[] { "pendingPick", "noCandidate", "rejectedPick", "neverProposed" }
                .Select(s => states.TryGetValue(s, out var v) ? new PipelineLinkStateCount(s, v.Foods, v.Lines) : new PipelineLinkStateCount(s, 0, 0))
                .ToList();

            var buckets = new (string Name, int Min, int Max)[] { ("1", 1, 1), ("2-4", 2, 4), ("5-19", 5, 19), ("20-99", 20, 99), ("100+", 100, int.MaxValue) }
                .Select(b =>
                {
                    var inBucket = unresolved.Where(u => u.IngredientLines >= b.Min && u.IngredientLines <= b.Max).ToList();
                    return new PipelineWordingBucket(b.Name, inBucket.Count, inBucket.Sum(u => u.IngredientLines));
                })
                .ToList();

            var topWordings = unresolved
                .OrderByDescending(u => u.IngredientLines).ThenBy(u => u.IngredientId)
                .Take(TopCount)
                .Select(u => (u.IngredientId, Lines: u.IngredientLines))
                .ToList();
            var itemWeightGaps = await ConversionGapQuery(_db, RecipeNutritionService.CategoryCount).ToListAsync(cancellationToken);
            var densityGaps = await ConversionGapQuery(_db, RecipeNutritionService.CategoryVolume).ToListAsync(cancellationToken);
            var topItemWeight = Top(itemWeightGaps);
            var topDensity = Top(densityGaps);

            var ids = topWordings.Select(t => t.IngredientId).Concat(topItemWeight.Select(t => t.IngredientId)).Concat(topDensity.Select(t => t.IngredientId)).Distinct().ToList();
            var names = await _db.Ingredients.AsNoTracking().IgnoreQueryFilters()
                .Where(i => ids.Contains(i.Id))
                .ToDictionaryAsync(i => i.Id, i => i.Name, cancellationToken);

            List<PipelineStuckFood> Named(IEnumerable<(long IngredientId, int Lines)> top) => top
                .Select(t => new PipelineStuckFood(t.IngredientId, names.GetValueOrDefault(t.IngredientId, string.Empty), t.Lines))
                .OrderByDescending(f => f.Lines).ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var foods = new PipelineFoodCounts
            {
                Curated = foodRow?.Curated ?? 0,
                PendingReview = foodRow?.PendingReview ?? 0,
                NotCurated = foodRow?.NotCurated ?? 0,
                CuratedWithoutNutrition = withoutNutrition,
                CuratedWithoutUsdaLink = foodRow?.WithoutUsda ?? 0,
                CuratedWithoutFoodGroup = foodRow?.WithoutGroup ?? 0,
                FoodsMissingItemWeight = itemWeightGaps.Count,
                FoodsMissingDensity = densityGaps.Count,
                WordingsInUse = uses.Count,
                UnresolvedWordings = unresolved.Count,
                WordingsByUse = buckets,
                UnresolvedByLinkState = linkStates,
            };
            var stuck = new PipelineStuck
            {
                UnresolvedWordings = Named(topWordings),
                CountWithoutItemWeight = Named(topItemWeight),
                VolumeWithoutDensity = Named(topDensity),
            };
            return (foods, stuck);
        }

        private static List<(long IngredientId, int Lines)> Top(IEnumerable<FoodLines> gaps) => gaps
            .OrderByDescending(g => g.Lines).ThenBy(g => g.IngredientId)
            .Take(TopCount)
            .Select(g => (g.IngredientId, g.Lines))
            .ToList();

        private async Task<(PipelineProposalCounts, int ProposedDay)> ProposalsAsync(DateTime now, CancellationToken cancellationToken)
        {
            var row = await ProposalQuery(_db, now).FirstOrDefaultAsync(cancellationToken);

            var queue = await LinkQueueQuery(_db, _links, now - IngredientLinkService.RejectionRetryAge).CountAsync(cancellationToken);
            var (verifiedCap, verifiedPerFood) = VerifiedCaps();
            var verified = await _links.VerifiedAllowanceAsync(verifiedCap, verifiedPerFood, cancellationToken);
            var (nameCap, namePerFood) = NameMatchCaps();
            var names = await _autoApply.AllowanceAsync(IngredientLinkService.ExactSource, nameCap, namePerFood, cancellationToken);

            var pending = row?.Pending ?? 0;
            var picks = row?.ModelPicks ?? 0;
            var exact = row?.Exact ?? 0;
            return (new PipelineProposalCounts
            {
                Pending = pending,
                PendingModelPicks = picks,
                PendingModelPicksVerified = row?.Verified ?? 0,
                PendingExactMatches = exact,
                PendingOther = Math.Max(0, pending - picks - exact),
                LinkQueue = queue,
                VerifiedAppliedLast24h = verified.Applied,
                VerifiedDailyCap = verifiedCap,
                VerifiedPerFoodCap = verifiedPerFood,
                FoodsAtCap = verified.BusyTargets.Count,
                NameMatchAppliedLast24h = names.Applied,
                NameMatchDailyCap = nameCap,
                NameMatchPerFoodCap = namePerFood,
                NameMatchFoodsAtCap = names.BusyTargets.Count,
                LastProposedAt = row?.LastProposed,
                LastAppliedBySystemAt = row?.LastApplied,
            }, row?.ProposedDay ?? 0);
        }

        private (int Daily, int PerFood) VerifiedCaps() => (
            Math.Max(0, _configuration.GetValue("IngredientLinking:MaxVerifiedAppliesPerDay", 500)),
            Math.Max(1, _configuration.GetValue("IngredientLinking:MaxVerifiedAppliesPerFoodPerDay", 25)));

        private (int Daily, int PerFood) NameMatchCaps() => (
            Math.Max(0, _configuration.GetValue("IngredientLinking:MaxNameMatchAppliesPerDay", 200)),
            Math.Max(1, _configuration.GetValue("IngredientLinking:MaxNameMatchAppliesPerFoodPerDay", 25)));

        private async Task<PipelineImageCounts> ImagesAsync(RecipeRow recipeRow, CancellationToken cancellationToken)
        {
            var candidates = await _db.RecipeImageCandidates.AsNoTracking()
                .GroupBy(c => c.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToDictionaryAsync(c => c.Status, c => c.Count, cancellationToken);
            int Of(string status) => candidates.GetValueOrDefault(status);
            return new PipelineImageCounts
            {
                ApprovedWithoutImage = recipeRow.ApprovedWithoutImage,
                NeverSearched = recipeRow.ApprovedNeverSearched,
                LastSearchedAt = recipeRow.LastImageSearchedAt,
                Candidates = new PipelineImageCandidateCounts
                {
                    Pending = Of(RecipeImageCandidateStatus.Pending),
                    Attached = Of(RecipeImageCandidateStatus.Attached),
                    Approved = Of(RecipeImageCandidateStatus.Approved),
                    Rejected = Of(RecipeImageCandidateStatus.Rejected),
                    Superseded = Of(RecipeImageCandidateStatus.Superseded),
                    Removed = Of(RecipeImageCandidateStatus.Removed),
                },
            };
        }

        private async Task<IReadOnlyList<PipelineLane>> LanesAsync(DateTime now, PipelineRecipeCounts recipes, RecipeRow recipeRow,
            PipelineProposalCounts proposals, int proposedDay, PipelineImageCounts images, IReadOnlyList<PipelineJournalRow> journal, CancellationToken cancellationToken)
        {
            var repairQueue = await RepairQueueQuery(_db).CountAsync(cancellationToken);
            var classifyQueue = await ClassifyQueueQuery(_db).CountAsync(cancellationToken);
            var lastLabel = await LastLabelQuery(_db).MaxAsync(cancellationToken);

            (DateTime? Last, int Day) Journal(params string[] changeTypes)
            {
                var rows = journal.Where(j => changeTypes.Contains(j.ChangeType)).ToList();
                return (rows.Max(j => j.LastAt), rows.Sum(j => j.Last24h));
            }
            bool On(string key, bool fallback) => _configuration.GetValue(key, fallback);
            PipelineLaneSwitch Switch(string section, string key, string label, bool fallback) => new(key, label, On(section + ":" + key, fallback));

            var stepSplit = Journal(ScrapedRecipeRepairService.StepSplitChangeType);
            var repair = Journal(RecipeAiRepairService.AttemptChangeType);
            var classify = Journal(RecipeLineClassificationService.AttemptChangeType, RecipeNonFoodScreeningService.AttemptChangeType);
            var (verifiedCap, verifiedPerFood) = VerifiedCaps();
            var (nameCap, namePerFood) = NameMatchCaps();
            var rewriteModel = _configuration["Ai:RewriteModel"] ?? _configuration["Ai:Model"] ?? "qwen2.5:3b";
            var rewriteConfigured = !string.IsNullOrWhiteSpace(_configuration["Ai:BatchOllamaUrl"]);

            return new List<PipelineLane>
            {
                new()
                {
                    Key = "intake", Name = "Recipe harvest", External = true, Enabled = true,
                    LastActivityAt = recipes.LastScrapedAt, Queue = null,
                    ActivityLast24h = null,
                },
                new()
                {
                    Key = "scraped-repair", Name = "Scraped recipe repair",
                    Enabled = On("ScrapedRepair:Enabled", true),
                    LastActivityAt = stepSplit.Last, ActivityLast24h = stepSplit.Day,
                },
                new()
                {
                    Key = "ai-repair", Name = "AI recipe repair", GateLane = AiLanes.Repair,
                    Enabled = On("Curation:AiRepair:Enabled", true), UsesModel = true,
                    ModelConfigured = _repairModel.IsConfigured, Model = _repairModel.IsConfigured ? _repairModel.ModelName : null,
                    Queue = repairQueue, QueueLabel = "recipes needing revision not yet tried at this version",
                    LastActivityAt = repair.Last, ActivityLast24h = repair.Day,
                },
                new()
                {
                    Key = "line-classify", Name = "Line classification and non-food screening", GateLane = AiLanes.LineClassify,
                    Enabled = On("Curation:LineClassify:Enabled", true), UsesModel = true,
                    ModelConfigured = _classificationModel.IsConfigured, Model = _classificationModel.IsConfigured ? _classificationModel.ModelName : null,
                    Switches = new[]
                    {
                        Switch("Curation:LineClassify", "ScreenNonFood", "Screen non-food recipes", true),
                        Switch("Curation:LineClassify", "ModelMayExempt", "Model may exempt a line", false),
                    },
                    Queue = classifyQueue, QueueLabel = "recipes with an unlabelled line without an amount, not yet tried at this version",
                    LastActivityAt = classify.Last, ActivityLast24h = classify.Day,
                },
                new()
                {
                    Key = "ingredient-link", Name = "Ingredient link picker and verifier", GateLane = AiLanes.Link,
                    Enabled = On("IngredientLinking:Enabled", true), UsesModel = true,
                    ModelConfigured = _matcher.IsConfigured, Model = _matcher.IsConfigured ? _matcher.ModelName : null,
                    Switches = new[]
                    {
                        Switch("IngredientLinking", "AutoApplyCertain", "Apply staples and same-name attaches", true),
                        Switch("IngredientLinking", "AutoApplyNameMatches", "Apply name matches", false),
                        Switch("IngredientLinking", "AutoApplyVerifiedAi", "Apply verified model picks", false),
                        Switch("IngredientLinking", "VerifyPending", "Verify pending model picks", false),
                        Switch("IngredientLinking", "HoldGuard", "Hold rules", true),
                    },
                    Caps = new[]
                    {
                        new PipelineLaneCap("Verified model picks per day", verifiedCap, proposals.VerifiedAppliedLast24h),
                        new PipelineLaneCap("Verified model picks per food per day", verifiedPerFood, null),
                        new PipelineLaneCap("Name matches per day", nameCap, proposals.NameMatchAppliedLast24h),
                        new PipelineLaneCap("Name matches per food per day", namePerFood, null),
                    },
                    Queue = proposals.LinkQueue, QueueLabel = "foods in use waiting for a link proposal",
                    LastActivityAt = proposals.LastProposedAt, ActivityLast24h = proposedDay,
                },
                new()
                {
                    Key = "prose-rewrite", Name = "Prose rewrite", GateLane = AiLanes.ProseRewrite,
                    Enabled = rewriteConfigured, UsesModel = true,
                    ModelConfigured = rewriteConfigured, Model = rewriteConfigured ? rewriteModel : null,
                    Switches = new[]
                    {
                        Switch("ProseRewrite", "CheckFidelity", "Fidelity check", true),
                        Switch("ProseRewrite", "DrainWhileFull", "Drain while full", true),
                    },
                    Queue = recipeRow.ProseQueue, QueueLabel = "quarantined recipes due a rewrite attempt",
                    LastActivityAt = recipeRow.LastProseAttemptAt, ActivityLast24h = recipeRow.ProseAttemptsDay,
                },
                new()
                {
                    Key = "nutrition-recalc", Name = "Nutrition label recalculation",
                    Enabled = On("NutritionRecalc:Enabled", true),
                    Queue = recipes.WithoutNutritionLabel, QueueLabel = "recipes using a food with nutrients and no label",
                    LastActivityAt = lastLabel,
                },
                new()
                {
                    Key = "auto-approve", Name = "Auto-approval",
                    Enabled = On("Curation:AutoApprove:Enabled", true),
                    Switches = new[]
                    {
                        Switch("Curation:AutoApprove", "RequireOwnImage", "Require an own image for unknown licences", true),
                        Switch("Curation:AutoApprove", "SkipUnchanged", "Skip unchanged sweeps", true),
                    },
                    Caps = new[]
                    {
                        new PipelineLaneCap("Unquantified lines allowed", Math.Max(0, _configuration.GetValue("Curation:AutoApprove:MaxUnquantifiedLines", 0)), null),
                    },
                    Queue = recipes.WaitingBlockedBy.None, QueueLabel = "waiting recipes with no unresolved food",
                    LastActivityAt = recipes.LastAutoApprovedAt, ActivityLast24h = recipes.AutoApprovedLast24h,
                },
                new()
                {
                    Key = "dish-grouping", Name = "Dish grouping", GateLane = AiLanes.DishGrouping,
                    Enabled = On("DishGrouping:Enabled", true), UsesModel = true,
                    Switches = new[] { Switch("DishGrouping", "DrainWhileFull", "Drain while full", true) },
                    Queue = recipeRow.Ungrouped, QueueLabel = "recipes without a dish group",
                },
                new()
                {
                    Key = "tool-tagging", Name = "Kitchen tool tagging", GateLane = AiLanes.ToolTagging,
                    Enabled = On("ToolTagging:Enabled", true), UsesModel = true,
                    ModelConfigured = _toolTagger.IsConfigured, Model = _toolTagger.IsConfigured ? _toolTagger.ModelName : null,
                    Queue = recipeRow.Untagged, QueueLabel = "recipes not yet tagged",
                    LastActivityAt = recipeRow.LastToolsTaggedAt, ActivityLast24h = recipeRow.ToolsTaggedDay,
                },
                new()
                {
                    Key = "image-finder", Name = "Recipe image finder", External = true, Enabled = true,
                    Queue = images.ApprovedWithoutImage, QueueLabel = "approved recipes without an image",
                    LastActivityAt = images.LastSearchedAt,
                },
            };
        }

        /// <summary>The per-recipe counts, in one pass over the recipes.</summary>
        public static IQueryable<RecipeRow> RecipeQuery(ApplicationDbContext db, DateTime now, DateTime proseRetryBefore)
        {
            var day = now.AddDays(-1);
            var week = now.AddDays(-7);
            return db.Recipes.AsNoTracking()
                .GroupBy(r => 1)
                .Select(g => new RecipeRow
                {
                    Approved = g.Count(r => r.CurationStatusId == Curated),
                    Waiting = g.Count(r => r.CurationStatusId == NonCurated),
                    NeedsRevision = g.Count(r => r.CurationStatusId == RequiresRevision),
                    PendingReview = g.Count(r => r.CurationStatusId == PendingCuration),
                    Rejected = g.Count(r => r.CurationStatusId == Rejected),
                    WaitingPublicDomain = g.Count(r => r.CurationStatusId == NonCurated && r.LicenseStatus == RecipeLicenseStatus.PublicDomain),
                    WaitingScrapedWithoutOwnImage = g.Count(r => r.CurationStatusId == NonCurated && r.LicenseStatus == RecipeLicenseStatus.Unknown
                        && r.ScrapedAtUtc != null && (r.Image == null || r.Image == "")),
                    WaitingWithSourceProse = g.Count(r => r.CurationStatusId == NonCurated && r.ContainsSourceProse),
                    RevisionMissingQuantities = g.Count(r => r.CurationStatusId == RequiresRevision && r.VettingIssues != null
                        && r.VettingIssues.Contains(RecipeAiRepairService.QuantityIssueMarker)),
                    RevisionSteps = g.Count(r => r.CurationStatusId == RequiresRevision && r.VettingIssues != null
                        && r.VettingIssues.Contains(RecipeAiRepairService.StepsIssueMarker)),
                    ApprovedWithoutImage = g.Count(r => r.CurationStatusId == Curated && (r.Image == null || r.Image == "")),
                    ApprovedNeverSearched = g.Count(r => r.CurationStatusId == Curated && (r.Image == null || r.Image == "") && r.ImageSearchedAt == null),
                    LastImageSearchedAt = g.Max(r => r.ImageSearchedAt),
                    LastScrapedAt = g.Max(r => r.ScrapedAtUtc),
                    CreatedLast7d = g.Count(r => r.CreatedDate >= week),
                    ProseQueue = g.Count(r => r.ContainsSourceProse && (r.ProseRewriteAttemptedAt == null || r.ProseRewriteAttemptedAt < proseRetryBefore)),
                    LastProseAttemptAt = g.Max(r => r.ProseRewriteAttemptedAt),
                    ProseAttemptsDay = g.Count(r => r.ProseRewriteAttemptedAt >= day),
                    Ungrouped = g.Count(r => r.DishGroupId == null),
                    Untagged = g.Count(r => r.ToolsTaggedAt == null),
                    LastToolsTaggedAt = g.Max(r => r.ToolsTaggedAt),
                    ToolsTaggedDay = g.Count(r => r.ToolsTaggedAt >= day),
                });
        }

        /// <summary>The latest auto-approval and the number in the last day, from the approvals' curation feedback.</summary>
        public static IQueryable<ActivityRow> AutoApprovalQuery(ApplicationDbContext db, DateTime now)
        {
            var day = now.AddDays(-1);
            return db.CurationFeedbacks.AsNoTracking()
                .Where(f => f.FeedbackNotes != null && f.FeedbackNotes.StartsWith(AutoApprovedNote))
                .GroupBy(f => 1)
                .Select(g => new ActivityRow { Last = g.Max(f => (DateTime?)f.CreatedDate), Day = g.Count(f => f.CreatedDate >= day) });
        }

        /// <summary>When a derived label was last written; a count of recipes labelled per day would cost a distinct pass over every label row.</summary>
        public static IQueryable<DateTime?> LastLabelQuery(ApplicationDbContext db) => db.RecipeNutrition.AsNoTracking()
            .Select(n => n.DateCalculated);

        public static IQueryable<FoodRow> FoodQuery(ApplicationDbContext db) => db.Ingredients.AsNoTracking()
            .GroupBy(i => 1)
            .Select(g => new FoodRow
            {
                Curated = g.Count(i => i.CurationStatusId == Curated),
                PendingReview = g.Count(i => i.CurationStatusId == PendingCuration),
                NotCurated = g.Count(i => i.CurationStatusId == NonCurated),
                WithoutUsda = g.Count(i => i.CurationStatusId == Curated && i.FdcId == null),
                WithoutGroup = g.Count(i => i.CurationStatusId == Curated && i.FoodGroupId == null),
            });

        public static IQueryable<IngredientEntity> CuratedWithoutNutritionQuery(ApplicationDbContext db) => db.Ingredients.AsNoTracking()
            .Where(i => i.CurationStatusId == Curated && !i.IngredientNutrients.Any());

        /// <summary>Foods with a pending USDA link proposal.</summary>
        public static IQueryable<long> PendingLinkFoodsQuery(ApplicationDbContext db) => db.FoodCatalogProposals.AsNoTracking()
            .Where(p => p.Field == FoodCatalogReviewService.FdcLinkField && p.Status == FoodProposalStatus.Pending && p.IngredientId != null)
            .Select(p => p.IngredientId!.Value)
            .Distinct();

        /// <summary>Foods with a rejected USDA link proposal, and whether the rejection was for no candidate.</summary>
        public static IQueryable<RejectedLink> RejectedLinkFoodsQuery(ApplicationDbContext db) => db.FoodCatalogProposals.AsNoTracking()
            .Where(p => p.Field == FoodCatalogReviewService.FdcLinkField && p.Status == FoodProposalStatus.Rejected && p.IngredientId != null)
            .GroupBy(p => p.IngredientId!.Value)
            .Select(g => new RejectedLink { IngredientId = g.Key, NoCandidate = g.Count(p => p.Source == IngredientLinkService.NoCandidateSource) > 0 });

        public static IQueryable<ProposalRow> ProposalQuery(ApplicationDbContext db, DateTime now)
        {
            var day = now.AddDays(-1);
            return db.FoodCatalogProposals.AsNoTracking()
                .GroupBy(p => 1)
                .Select(g => new ProposalRow
                {
                    Pending = g.Count(p => p.Status == FoodProposalStatus.Pending),
                    ModelPicks = g.Count(p => p.Status == FoodProposalStatus.Pending && p.Field == FoodCatalogReviewService.FdcLinkField && p.Source.StartsWith("ai:")),
                    Verified = g.Count(p => p.Status == FoodProposalStatus.Pending && p.Field == FoodCatalogReviewService.FdcLinkField && p.Source.StartsWith("ai:")
                        && p.Reason != null && p.Reason.Contains(IngredientLinkService.VerifiedMark)),
                    Exact = g.Count(p => p.Status == FoodProposalStatus.Pending && p.Field == FoodCatalogReviewService.FdcLinkField && p.Source == IngredientLinkService.ExactSource),
                    LastProposed = g.Where(p => p.Batch == IngredientLinkService.Batch).Max(p => (DateTime?)p.CreatedDate),
                    ProposedDay = g.Count(p => p.Batch == IngredientLinkService.Batch && p.CreatedDate >= day),
                    LastApplied = g.Where(p => p.Status == FoodProposalStatus.Applied && p.ReviewedByPersonId == SystemConstants.SystemPersonId).Max(p => p.ReviewedAt),
                });
        }

        /// <summary>
        /// Every live recipe line, by the label rule of <see cref="RecipeNutritionService.StatusFor"/>
        /// and the metrics.sql definitions, in one pass grouped by whether the recipe is approved.
        /// Whether a food has nutrients is read from one set of food ids joined to the lines.
        /// </summary>
        public static IQueryable<FunnelRow> LineFunnelQuery(ApplicationDbContext db) => LineFacts(db)
                .GroupBy(l => l.Approved)
                .Select(g => new FunnelRow
                {
                    Approved = g.Key,
                    Total = g.Count(),
                    NotAnIngredient = g.Count(l => !l.Ingredient),
                    Ingredient = g.Count(l => l.Ingredient),
                    OnCuratedFood = g.Count(l => l.Ingredient && l.OnCurated),
                    NotMeasured = g.Count(l => l.Ingredient && !l.Measured),
                    NoData = g.Count(l => l.Ingredient && l.Measured && !l.HasData),
                    Counted = g.Count(l => l.Ingredient && l.Measured && l.HasData && l.Convertible),
                    CountedByServingFallback = g.Count(l => l.Ingredient && l.Measured && l.HasData && l.Convertible
                        && l.Category == RecipeNutritionService.CategoryCount && !(l.GramsPerItem > 0)),
                    CountWithoutItemWeight = g.Count(l => l.Ingredient && l.OnCurated && l.Measured
                        && l.Category == RecipeNutritionService.CategoryCount && l.GramsPerItem == null),
                    VolumeWithoutDensity = g.Count(l => l.Ingredient && l.OnCurated && l.Measured
                        && l.Category == RecipeNutritionService.CategoryVolume && l.GramsPerMilliliter == null),
                    CountableStrict = g.Count(l => l.Ingredient && l.OnCurated && l.Measured
                        && (l.Category == RecipeNutritionService.CategoryMass
                            || (l.Category == RecipeNutritionService.CategoryVolume && l.GramsPerMilliliter != null)
                            || (l.Category == RecipeNutritionService.CategoryCount && l.GramsPerItem != null))),
                    Folded = g.Count(l => l.Ingredient && l.Folded),
                });

        /// <summary>
        /// Approved recipes holding a measured ingredient line their label leaves out, by the same
        /// rule as <see cref="RecipeNutritionService.IsUncountedLine"/>, read from the line facts
        /// rather than per line, which costs a nutrient lookup for every measured line in the database.
        /// </summary>
        public static IQueryable<long> ApprovedWithUncountedLineQuery(ApplicationDbContext db) => LineFacts(db)
            .Where(l => l.Approved && l.Ingredient && l.Measured && !(l.HasData && l.Convertible))
            .Select(l => l.RecipeId)
            .Distinct();

        /// <summary>
        /// Every live line of a live recipe with what the label rule of
        /// <see cref="RecipeNutritionService.StatusFor"/> and the metrics.sql series look at. Whether a
        /// food has nutrients is read from one set of food ids joined to the lines.
        /// </summary>
        private static IQueryable<LineFact> LineFacts(ApplicationDbContext db)
        {
            var withNutrients = db.IngredientNutrients.Select(n => (long?)n.IngredientId).Distinct();
            return
                from ri in db.RecipeIngredients.AsNoTracking()
                join r in db.Recipes on ri.RecipeId equals r.Id
                join i in db.Ingredients on ri.IngredientId equals i.Id
                join m in db.Set<MeasurementEntity>() on ri.MeasurementId equals m.Id
                join n in withNutrients on (long?)ri.IngredientId equals n into found
                from n in found.DefaultIfEmpty()
                select new LineFact
                {
                    RecipeId = ri.RecipeId,
                    Approved = r.CurationStatusId == Curated,
                    Ingredient = ri.LineKind == null || ri.LineKind == IngredientKind,
                    OnCurated = i.CurationStatusId == Curated,
                    Measured = ri.Quantity > 0,
                    HasData = n != null,
                    Category = m.MeasurementCategoryId,
                    Convertible = (m.BaseUnitConversionFactor ?? 1m) > 0
                        && (m.MeasurementCategoryId == RecipeNutritionService.CategoryMass
                            || (m.MeasurementCategoryId == RecipeNutritionService.CategoryVolume && i.GramsPerMilliliter > 0)
                            || (m.MeasurementCategoryId == RecipeNutritionService.CategoryCount && !m.IsContainer
                                && (i.GramsPerItem > 0 || i.ReferenceServingGrams > 0))),
                    GramsPerItem = i.GramsPerItem,
                    GramsPerMilliliter = i.GramsPerMilliliter,
                    Folded = ri.RawLine.Contains(" + "),
                };
        }

        private sealed class LineFact
        {
            public long RecipeId { get; init; }
            public bool Approved { get; init; }
            public bool Ingredient { get; init; }
            public bool OnCurated { get; init; }
            public bool Measured { get; init; }
            public bool HasData { get; init; }
            public long Category { get; init; }
            public bool Convertible { get; init; }
            public decimal? GramsPerItem { get; init; }
            public decimal? GramsPerMilliliter { get; init; }
            public bool Folded { get; init; }
        }

        /// <summary>For each waiting recipe that has one, the number of distinct uncurated foods on its ingredient lines.</summary>
        public static IQueryable<int> BlockedByRecipeQuery(ApplicationDbContext db) => db.RecipeIngredients.AsNoTracking()
            .Where(ri => !ri.Recipe.IsDeleted && ri.Recipe.CurationStatusId == NonCurated
                && (ri.LineKind == null || ri.LineKind == IngredientKind)
                && ri.Ingredient.CurationStatusId != Curated)
            .GroupBy(ri => ri.RecipeId)
            .Select(g => g.Select(ri => ri.IngredientId).Distinct().Count());

        /// <summary>Each uncurated food on a live line, with its lines and its ingredient lines.</summary>
        public static IQueryable<FoodUse> UnresolvedByFoodQuery(ApplicationDbContext db) => db.RecipeIngredients.AsNoTracking()
            .Where(ri => !ri.Recipe.IsDeleted && ri.Ingredient.CurationStatusId != Curated)
            .GroupBy(ri => ri.IngredientId)
            .Select(g => new FoodUse
            {
                IngredientId = g.Key,
                Lines = g.Count(),
                IngredientLines = g.Count(ri => ri.LineKind == null || ri.LineKind == IngredientKind),
            });

        /// <summary>
        /// Curated foods whose measured ingredient lines in the given category have no conversion:
        /// pieces without an item weight, or volumes without a density (metrics.sql).
        /// </summary>
        public static IQueryable<FoodLines> ConversionGapQuery(ApplicationDbContext db, long categoryId)
        {
            var lines = db.RecipeIngredients.AsNoTracking()
                .Where(ri => !ri.Recipe.IsDeleted && (ri.LineKind == null || ri.LineKind == IngredientKind)
                    && ri.Quantity > 0 && ri.Ingredient.CurationStatusId == Curated
                    && ri.Measurement!.MeasurementCategoryId == categoryId);
            lines = categoryId == RecipeNutritionService.CategoryCount
                ? lines.Where(ri => ri.Ingredient.GramsPerItem == null)
                : lines.Where(ri => ri.Ingredient.GramsPerMilliliter == null);
            return lines
                .GroupBy(ri => ri.IngredientId)
                .Select(g => new FoodLines { IngredientId = g.Key, Lines = g.Count() });
        }

        /// <summary>Live recipes that use a food with nutrients and have no nutrition rows.</summary>
        public static IQueryable<RecipeEntity> WithoutNutritionLabelQuery(ApplicationDbContext db)
        {
            var withNutrients = db.IngredientNutrients.Select(n => n.IngredientId).Distinct();
            return db.Recipes.AsNoTracking()
                .Where(r => !r.Nutrition!.Any() && r.RecipeIngredients!.Any(ri => withNutrients.Contains(ri.IngredientId)));
        }

        /// <summary>Audit log rows by entity and change type, with the last day, the last week and the latest time.</summary>
        public static IQueryable<PipelineJournalRow> JournalQuery(ApplicationDbContext db, DateTime now)
        {
            var day = now.AddDays(-1);
            var week = now.AddDays(-7);
            return db.AuditLogEntries.AsNoTracking()
                .GroupBy(a => new { a.EntityType, a.ChangeType })
                .Select(g => new PipelineJournalRow(
                    g.Key.EntityType,
                    g.Key.ChangeType,
                    g.Count(a => a.Timestamp >= day),
                    g.Count(a => a.Timestamp >= week),
                    g.Count(),
                    g.Max(a => (DateTime?)a.Timestamp)));
        }

        /// <summary>The foods the link lane would ask about, by the lane's own rule, that a live line uses.</summary>
        public static IQueryable<IngredientEntity> LinkQueueQuery(ApplicationDbContext db, IIngredientLinkService links, DateTime retryBefore) =>
            links.WaitingSources(retryBefore).Where(i => db.RecipeIngredients.Any(ri => ri.IngredientId == i.Id));

        /// <summary>The AI repair lane's candidates (RecipeAiRepairService.RepairBatchAsync) not yet tried at its version.</summary>
        public static IQueryable<RecipeEntity> RepairQueueQuery(ApplicationDbContext db) => db.Recipes.AsNoTracking()
            .Where(r => r.CurationStatusId == RequiresRevision
                && r.DateCurationCompleted == null
                && r.ScrapedAtUtc != null
                && r.VettingIssues != null
                && !db.AuditLogEntries.Any(a => a.EntityType == "Recipe" && a.EntityId == r.Id && a.ChangeType == RecipeAiRepairService.AttemptChangeType
                    && a.NewValue != null && a.NewValue.StartsWith(RecipeAiRepairService.AttemptVersionPrefix))
                && !db.CurationFeedbacks.Any(f => f.EntityId == r.Id && f.EntityType!.Name == "Recipe" && f.AdminId != SystemConstants.SystemPersonId));

        /// <summary>The line classification lane's candidates (RecipeLineClassificationService.ClassifyBatchAsync) not yet tried at its version.</summary>
        public static IQueryable<RecipeEntity> ClassifyQueueQuery(ApplicationDbContext db) => db.Recipes.AsNoTracking()
            .Where(r => (r.CurationStatusId == RequiresRevision || r.CurationStatusId == NonCurated)
                && r.DateCurationCompleted == null
                && r.RecipeIngredients!.Any(ri => ri.Quantity <= 0 && ri.LineKind == null)
                && !db.AuditLogEntries.Any(a => a.EntityType == "Recipe" && a.EntityId == r.Id && a.ChangeType == RecipeLineClassificationService.AttemptChangeType
                    && a.NewValue != null && a.NewValue.StartsWith(RecipeLineClassificationService.AttemptVersionPrefix))
                && !db.CurationFeedbacks.Any(f => f.EntityId == r.Id && f.EntityType!.Name == "Recipe" && f.AdminId != SystemConstants.SystemPersonId));

        private static FunnelRow Add(FunnelRow a, FunnelRow b) => new()
        {
            Total = a.Total + b.Total,
            NotAnIngredient = a.NotAnIngredient + b.NotAnIngredient,
            Ingredient = a.Ingredient + b.Ingredient,
            OnCuratedFood = a.OnCuratedFood + b.OnCuratedFood,
            NotMeasured = a.NotMeasured + b.NotMeasured,
            NoData = a.NoData + b.NoData,
            Counted = a.Counted + b.Counted,
            CountedByServingFallback = a.CountedByServingFallback + b.CountedByServingFallback,
            CountWithoutItemWeight = a.CountWithoutItemWeight + b.CountWithoutItemWeight,
            VolumeWithoutDensity = a.VolumeWithoutDensity + b.VolumeWithoutDensity,
            CountableStrict = a.CountableStrict + b.CountableStrict,
            Folded = a.Folded + b.Folded,
        };

        private static PipelineLineFunnel ToFunnel(FunnelRow r) => new()
        {
            Total = r.Total,
            NotAnIngredient = r.NotAnIngredient,
            Ingredient = r.Ingredient,
            OnCuratedFood = r.OnCuratedFood,
            Unresolved = r.Ingredient - r.OnCuratedFood,
            NotMeasured = r.NotMeasured,
            NoData = r.NoData,
            NoConversion = r.Ingredient - r.NotMeasured - r.NoData - r.Counted,
            Counted = r.Counted,
            CountedByServingFallback = r.CountedByServingFallback,
            CountWithoutItemWeight = r.CountWithoutItemWeight,
            VolumeWithoutDensity = r.VolumeWithoutDensity,
            CountableStrict = r.CountableStrict,
        };

        public sealed class RecipeRow
        {
            public int Approved { get; init; }
            public int Waiting { get; init; }
            public int NeedsRevision { get; init; }
            public int PendingReview { get; init; }
            public int Rejected { get; init; }
            public int WaitingPublicDomain { get; init; }
            public int WaitingScrapedWithoutOwnImage { get; init; }
            public int WaitingWithSourceProse { get; init; }
            public int RevisionMissingQuantities { get; init; }
            public int RevisionSteps { get; init; }
            public int ApprovedWithoutImage { get; init; }
            public int ApprovedNeverSearched { get; init; }
            public DateTime? LastImageSearchedAt { get; init; }
            public DateTime? LastScrapedAt { get; init; }
            public int CreatedLast7d { get; init; }
            public int ProseQueue { get; init; }
            public DateTime? LastProseAttemptAt { get; init; }
            public int ProseAttemptsDay { get; init; }
            public int Ungrouped { get; init; }
            public int Untagged { get; init; }
            public DateTime? LastToolsTaggedAt { get; init; }
            public int ToolsTaggedDay { get; init; }
        }

        public sealed class FunnelRow
        {
            public bool Approved { get; init; }
            public int Total { get; init; }
            public int NotAnIngredient { get; init; }
            public int Ingredient { get; init; }
            public int OnCuratedFood { get; init; }
            public int NotMeasured { get; init; }
            public int NoData { get; init; }
            public int Counted { get; init; }
            public int CountedByServingFallback { get; init; }
            public int CountWithoutItemWeight { get; init; }
            public int VolumeWithoutDensity { get; init; }
            public int CountableStrict { get; init; }
            public int Folded { get; init; }
        }

        public sealed class ActivityRow
        {
            public DateTime? Last { get; init; }
            public int Day { get; init; }
        }

        public sealed class FoodRow
        {
            public int Curated { get; init; }
            public int PendingReview { get; init; }
            public int NotCurated { get; init; }
            public int WithoutUsda { get; init; }
            public int WithoutGroup { get; init; }
        }

        public sealed class RejectedLink
        {
            public long IngredientId { get; init; }
            public bool NoCandidate { get; init; }
        }

        public sealed class ProposalRow
        {
            public int Pending { get; init; }
            public int ModelPicks { get; init; }
            public int Verified { get; init; }
            public int Exact { get; init; }
            public DateTime? LastProposed { get; init; }
            public int ProposedDay { get; init; }
            public DateTime? LastApplied { get; init; }
        }

        public sealed class FoodUse
        {
            public long IngredientId { get; init; }
            public int Lines { get; init; }
            public int IngredientLines { get; init; }
        }

        public sealed class FoodLines
        {
            public long IngredientId { get; init; }
            public int Lines { get; init; }
        }
    }
}

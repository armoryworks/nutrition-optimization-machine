using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nom.Data;
using Nom.Data.Audit;
using Nom.Data.Curation;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Curation;
using Nom.Orch.Models.Recipe;
using Nom.Orch.Services.Support;

namespace Nom.Orch.Services
{
    /// <inheritdoc cref="IReprocessLaneService"/>
    public class ReprocessLaneService : IReprocessLaneService
    {
        public const string Lane = AiLanes.Reprocess;
        public const string BatchEntityType = "ReprocessLane";
        public const string BatchChangeType = "ReprocessBatch";

        /// <summary>Changed lines a batch needs before its rule-check share can pause the lane.</summary>
        public const int MinimumLinesForShare = 50;

        /// <summary>Approved recipes a batch needs before its held share can pause the lane.</summary>
        public const int MinimumApprovedForShare = 20;

        public const double RuleShareToPause = 0.98;
        public const double RuleShareToQualify = 0.99;
        public const double ApprovedHeldToPause = 0.05;

        /// <summary>Rebuilt recipes of a live batch rebuilt again as a dry run (A5).</summary>
        public const int IdempotenceSample = 10;

        /// <summary>Changed lines the judge reads per sampled recipe.</summary>
        public const int JudgedLinesPerRecipe = 3;

        private const int SignalLines = 6;
        private const int MethodChars = 400;
        private const string CaloriesNutrient = "Calories";

        /// <summary>
        /// The rebuild tier qualifies on a Wilson lower bound of 98 % (at most one worse line in 300)
        /// and pauses when a full window of 300 audited rebuilds shows more than 3 % confirmed worse
        /// (reprocess.md section 3, observed-error rule of the lead's LQ-4).
        /// </summary>
        public static readonly LanePolicy RebuildPolicy = new(QualifyBar: 0.98, PauseBar: 0.03, PauseWindow: 300);

        /// <summary>The order tier changes positions only and is audited by an exact rule, so it is held to 99 %.</summary>
        public static readonly LanePolicy OrderPolicy = new(QualifyBar: 0.99, PauseBar: 0.02, PauseWindow: 400);

        public static readonly LaneSamplePlan RebuildSample = new(Minimum: 30, Rate: 0.3);
        public static readonly LaneSamplePlan OrderSample = new(Minimum: 30, Rate: 0.1);

        private static readonly string[] Order = { "order" };

        private readonly ApplicationDbContext _db;
        private readonly IRecipeReprocessService _reprocess;
        private readonly ILaneFrameService _frame;
        private readonly IReprocessLineJudge _judge;
        private readonly ILogger<ReprocessLaneService> _logger;

        public ReprocessLaneService(ApplicationDbContext db, IRecipeReprocessService reprocess, ILaneFrameService frame, IReprocessLineJudge judge, ILogger<ReprocessLaneService> logger)
        {
            _db = db;
            _reprocess = reprocess;
            _frame = frame;
            _judge = judge;
            _logger = logger;
        }

        public string Signature(string tier) => Hash(tier == ReprocessTier.Order
            ? $"{RecipeLineBuilder.Version}|{ReprocessRuleAudit.Version}|order-rule"
            : $"{RecipeLineBuilder.Version}|{ReprocessRuleAudit.Version}|{OllamaLineJudge.Signature}|{_judge.ModelName}");

        private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16].ToLowerInvariant();

        /// <summary>
        /// The candidates a tier takes next: read with the current line rules, with a reason a rebuild
        /// acts on, not yet decided (live, also those decided in shadow), highest priority first.
        /// </summary>
        public static IQueryable<ReprocessCandidateEntity> PendingQuery(ApplicationDbContext db, string tier, bool live) =>
            db.ReprocessCandidates
                .Where(c => c.Tier == tier && c.RuleVersion == RecipeLineBuilder.Version && (c.Reasons & ReprocessReason.Rebuildable) != 0
                    && (c.Outcome == null || (live && c.Outcome == ReprocessOutcome.Shadow)))
                .OrderByDescending(c => c.Priority).ThenBy(c => c.RecipeId);

        public async Task<ReprocessLaneBatchResult> RunBatchAsync(string tier, string mode, int max, CancellationToken cancellationToken = default)
        {
            var now = DateTime.UtcNow;
            var live = mode == LaneMode.Live;
            var batch = new ReprocessLaneBatchResult
            {
                Tier = tier, Mode = mode, Stamp = now, BatchId = RecipeReprocessService.NewBatchId(now, RecipeReprocessService.LaneBatchPrefix),
            };
            var candidates = await PendingQuery(_db, tier, live).AsNoTracking().Take(Math.Max(0, max)).ToListAsync(cancellationToken);
            if (candidates.Count == 0) return batch;

            var ids = candidates.Select(c => c.RecipeId).ToList();
            var recipes = await _db.Recipes.AsNoTracking().Where(r => ids.Contains(r.Id))
                .Select(r => new
                {
                    r.Id, r.Name, r.CurationStatusId, r.Servings, r.RecipeServings,
                    ModelMayRead = !r.SourceAiReserved || (r.ProseLaneVersion != null && r.ProseAttempts == 0 && !r.ProseWithheld && !r.ContainsSourceProse),
                })
                .ToDictionaryAsync(r => r.Id, cancellationToken);
            var rebuildTier = tier != ReprocessTier.Order;
            var rowsBefore = rebuildTier
                ? (await _db.RecipeIngredients.AsNoTracking().Where(ri => ids.Contains(ri.RecipeId))
                    .Select(ri => new { ri.RecipeId, ri.IngredientId, ri.Quantity, ri.MeasurementId, ri.LineKind }).ToListAsync(cancellationToken))
                    .ToLookup(ri => ri.RecipeId)
                : null;
            var result = await _reprocess.RebuildAsync(new RecipeReprocessRequestModel { RecipeIds = ids }, apply: live, orderOnly: tier == ReprocessTier.Order, batch.BatchId);

            var measures = await _db.Measurements.AsNoTracking().ToDictionaryAsync(m => m.Id, cancellationToken);
            var units = measures.ToDictionary(m => m.Key, m => m.Value.Name);
            var foodIds = result.Results.SelectMany(r => r.Lines).Select(l => l.NewIngredientId ?? 0)
                .Concat(rowsBefore?.SelectMany(g => g).Select(ri => ri.IngredientId) ?? Enumerable.Empty<long>())
                .Where(id => id > 0).Distinct().ToList();
            var foods = await _db.Ingredients.IgnoreQueryFilters().AsNoTracking().Where(i => foodIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, cancellationToken);
            var grams = foods.ToDictionary(f => f.Key, f => f.Value.GramsPerItem);
            var kcal = rebuildTier
                ? (await _db.IngredientNutrients.AsNoTracking().Where(n => foodIds.Contains(n.IngredientId) && n.Nutrient.Name == CaloriesNutrient)
                    .Select(n => new { n.IngredientId, n.Amount }).ToListAsync(cancellationToken))
                    .GroupBy(n => n.IngredientId).ToDictionary(g => g.Key, g => g.First().Amount)
                : new Dictionary<long, decimal>();
            decimal? KcalPerServing(IEnumerable<(long Food, decimal Quantity, long Measurement, string? Kind)> rows, decimal? servings) =>
                ReprocessRuleAudit.KcalPerServing(rows.Select(r => new ReprocessRuleAudit.Portion(
                    RecipeNutritionService.GramsFor(r.Quantity, measures.GetValueOrDefault(r.Measurement), foods.GetValueOrDefault(r.Food)),
                    kcal.TryGetValue(r.Food, out var perHundred) ? perHundred : null, r.Kind)), servings);
            var steps = (await _db.RecipeSteps.AsNoTracking().Where(s => ids.Contains(s.RecipeId)).OrderBy(s => s.StepNumber)
                    .Select(s => new { s.RecipeId, s.Description }).ToListAsync(cancellationToken))
                .ToLookup(s => s.RecipeId, s => s.Description ?? string.Empty);
            var byRecipe = candidates.ToDictionary(c => c.RecipeId);
            var signature = Signature(tier);
            var rebuiltIds = new List<long>();

            foreach (var one in result.Results)
            {
                batch.Taken++;
                var approved = recipes.TryGetValue(one.RecipeId, out var recipe) && recipe.CurationStatusId == (long)CurationStatusEnum.Curated;
                if (approved) batch.ApprovedTaken++;
                switch (one.Outcome)
                {
                    case RecipeReprocessOutcome.Rebuilt: batch.Rebuilt++; rebuiltIds.Add(one.RecipeId); break;
                    case RecipeReprocessOutcome.Unchanged: batch.Unchanged++; break;
                    case RecipeReprocessOutcome.Failed: batch.Failed++; break;
                    default:
                        batch.Held++;
                        if (approved) batch.ApprovedHeld++;
                        batch.HeldByReason[one.Reason ?? string.Empty] = batch.HeldByReason.GetValueOrDefault(one.Reason ?? string.Empty) + 1;
                        break;
                }

                var method = steps[one.RecipeId].ToList();
                var judgeMethod = recipe?.ModelMayRead == false ? new List<string>() : method;
                var lines = new List<ReprocessSignalLine>();
                int before = 0, after = 0;
                foreach (var line in one.Lines.Where(l => l.Changes.Count > 0 && !l.Changes.SequenceEqual(Order)))
                {
                    var unit = units.GetValueOrDefault(line.NewMeasurementId);
                    var oldUnit = line.OldMeasurementId is { } oldId ? units.GetValueOrDefault(oldId) : null;
                    var foodChanged = line.Changes.Contains("food") || line.Changes.Contains("added");
                    var gramsPerItem = line.NewIngredientId is { } foodId ? grams.GetValueOrDefault(foodId) : null;
                    var failures = ReprocessRuleAudit.Failures(
                        new ReprocessRuleAudit.Line(line.RawLine, line.NewFood, line.NewQuantity, unit, line.NewLineKind, line.FilledFromMethod, foodChanged, gramsPerItem), method);
                    after += failures.Count;
                    if (line.OldIngredientId != null)
                    {
                        var stated = IngredientLineParser.Parse(line.RawLine.Split(FoldedLine.Separator, 2)[0]) != null;
                        before += ReprocessRuleAudit.Failures(new ReprocessRuleAudit.Line(
                            line.RawLine, line.OldFood, line.OldQuantity ?? 0, oldUnit, line.OldLineKind, FilledFromMethod: !stated, FoodChanged: false), method).Count;
                    }

                    batch.LinesChecked++;
                    if (failures.Count > 0) batch.LinesFailing++;
                    foreach (var failure in failures) batch.FailuresByCheck[failure] = batch.FailuresByCheck.GetValueOrDefault(failure) + 1;
                    if (lines.Count < SignalLines)
                    {
                        lines.Add(new ReprocessSignalLine(
                            line.RawLine, line.OldFood, line.OldQuantity, oldUnit, line.OldLineKind,
                            line.NewFood, line.NewQuantity, unit, line.NewLineKind, line.FilledFromMethod, foodChanged, gramsPerItem,
                            Excerpt(ReprocessRuleAudit.MethodSentences(judgeMethod, line.NewFood, line.RawLine)), line.Changes.ToList(), failures));
                    }
                }

                var worse = after > before;
                if (worse) Fail(batch, ReprocessRuleAudit.NoWorse);

                var implausible = false;
                if (rebuildTier && one.Outcome == RecipeReprocessOutcome.Rebuilt && recipe != null)
                {
                    decimal? servings = recipe.Servings is > 0 ? (decimal)recipe.Servings.Value : recipe.RecipeServings is > 0 ? recipe.RecipeServings.Value : null;
                    var kcalBefore = KcalPerServing(rowsBefore![one.RecipeId].Select(r => (r.IngredientId, r.Quantity, r.MeasurementId, r.LineKind)), servings);
                    var kcalAfter = KcalPerServing(one.Lines.Where(l => l.NewIngredientId is > 0).Select(l => (l.NewIngredientId!.Value, l.NewQuantity, l.NewMeasurementId, l.NewLineKind)), servings);
                    implausible = !ReprocessRuleAudit.KcalPlausible(kcalBefore, kcalAfter);
                    if (implausible) Fail(batch, ReprocessRuleAudit.Plausible);
                }

                List<string>? sourceOrder = null, newOrder = null;
                if (!rebuildTier && one.Outcome == RecipeReprocessOutcome.Rebuilt && one.SourceLines != null)
                {
                    sourceOrder = one.SourceLines;
                    newOrder = one.Lines.OrderBy(l => l.SortOrder).Select(l => l.RawLine).ToList();
                    batch.LinesChecked++;
                    if (!ReprocessRuleAudit.FollowsSource(sourceOrder, newOrder))
                    {
                        batch.LinesFailing++;
                        batch.FailuresByCheck[ReprocessRuleAudit.InSourceOrder] = batch.FailuresByCheck.GetValueOrDefault(ReprocessRuleAudit.InSourceOrder) + 1;
                    }
                }

                var candidate = byRecipe[one.RecipeId];
                var reasons = ReprocessReason.All.Where(r => (candidate.Reasons & r.Flag) != 0).Select(r => r.Code).ToList();
                var writes = one.Outcome == RecipeReprocessOutcome.Rebuilt;
                await _frame.RecordAsync(new LaneDecisionInput(
                    Lane, tier, $"recipe:{one.RecipeId}",
                    one.Outcome + (one.Reason == null ? string.Empty : ": " + one.Reason),
                    mode, writes, signature, Stratum(candidate.Reasons),
                    new ReprocessSignals(one.RecipeId, recipe?.Name ?? string.Empty, one.Outcome, one.Reason, reasons, batch.BatchId, one.JournalId, worse, lines,
                        implausible, sourceOrder, newOrder)),
                    cancellationToken);

                candidate.Outcome = !live && writes ? ReprocessOutcome.Shadow : one.Outcome switch
                {
                    RecipeReprocessOutcome.Rebuilt => ReprocessOutcome.Rebuilt,
                    RecipeReprocessOutcome.Unchanged => ReprocessOutcome.Unchanged,
                    RecipeReprocessOutcome.Failed => ReprocessOutcome.Failed,
                    _ => ReprocessOutcome.Held,
                };
                candidate.OutcomeReason = one.Reason is { } reason ? (reason.Length > 255 ? reason[..255] : reason) : null;
                candidate.OutcomeAt = DateTime.UtcNow;
                candidate.JournalId = one.JournalId;
                candidate.LastModifiedDate = candidate.OutcomeAt;
                candidate.LastModifiedByPersonId = SystemConstants.SystemPersonId;
            }
            foreach (var row in await _db.ReprocessCandidates.Where(c => ids.Contains(c.RecipeId)).ToListAsync(cancellationToken))
            {
                var decided = byRecipe[row.RecipeId];
                (row.Outcome, row.OutcomeReason, row.OutcomeAt, row.JournalId, row.LastModifiedDate, row.LastModifiedByPersonId) =
                    (decided.Outcome, decided.OutcomeReason, decided.OutcomeAt, decided.JournalId, decided.LastModifiedDate, decided.LastModifiedByPersonId);
            }
            await _db.SaveChangesAsync(cancellationToken);

            if (live && rebuiltIds.Count > 0)
            {
                var again = await _reprocess.RebuildAsync(
                    new RecipeReprocessRequestModel { RecipeIds = rebuiltIds.Take(IdempotenceSample).ToList() }, apply: false, orderOnly: tier == ReprocessTier.Order);
                var moved = again.Results.Count(r => r.Outcome == RecipeReprocessOutcome.Rebuilt);
                if (moved > 0)
                {
                    batch.LinesChecked += moved;
                    batch.LinesFailing += moved;
                    batch.FailuresByCheck[ReprocessRuleAudit.Idempotent] = moved;
                    batch.Faults.Add($"{moved} of {again.Results.Count} rebuilt recipes change again when rebuilt once more (A5)");
                }
            }

            if (batch.LinesChecked >= MinimumLinesForShare && batch.RuleShare is { } share)
            {
                if (share < RuleShareToPause)
                    batch.Faults.Add(string.Create(CultureInfo.InvariantCulture, $"the rule checks passed on {share:P1} of {batch.LinesChecked} changed lines, under {RuleShareToPause:P0}"));
                else if (!live && share < RuleShareToQualify)
                    batch.Faults.Add(string.Create(CultureInfo.InvariantCulture, $"the rule checks passed on {share:P1} of {batch.LinesChecked} changed lines, under the {RuleShareToQualify:P0} a tier needs to go live"));
            }
            if (batch.ApprovedTaken >= MinimumApprovedForShare && (double)batch.ApprovedHeld / batch.ApprovedTaken > ApprovedHeldToPause)
                batch.Faults.Add(string.Create(CultureInfo.InvariantCulture, $"{batch.ApprovedHeld} of {batch.ApprovedTaken} approved recipes were held, over {ApprovedHeldToPause:P0}"));

            _db.AuditLogEntries.Add(new AuditLogEntryEntity
            {
                EntityType = BatchEntityType, EntityId = 0, ChangeType = BatchChangeType, PropertyName = tier,
                NewValue = Clip(LaneJournal.Write(batch), 4000), Timestamp = DateTime.UtcNow, ChangedByPersonId = SystemConstants.SystemPersonId,
            });
            await _db.SaveChangesAsync(cancellationToken);
            _db.ChangeTracker.Clear();
            _logger.LogInformation(
                "Reprocess lane {Tier} ({Mode}): {Taken} recipes, {Rebuilt} {Verb}, {Held} held, {Unchanged} unchanged, {Failed} failed; rule checks passed on {Passing}/{Checked} changed lines{Faults}",
                tier, mode, batch.Taken, batch.Rebuilt, live ? "rebuilt" : "would be rebuilt", batch.Held, batch.Unchanged, batch.Failed,
                batch.LinesChecked - batch.LinesFailing, batch.LinesChecked, batch.Faults.Count == 0 ? string.Empty : "; " + string.Join("; ", batch.Faults));
            return batch;
        }

        private static void Fail(ReprocessLaneBatchResult batch, string check)
        {
            batch.LinesChecked++;
            batch.LinesFailing++;
            batch.FailuresByCheck[check] = batch.FailuresByCheck.GetValueOrDefault(check) + 1;
        }

        private static string Excerpt(IEnumerable<string> sentences)
        {
            var text = string.Join(" ", sentences);
            return text.Length <= MethodChars ? text : text[..MethodChars];
        }

        private static string? Clip(string? value, int length) => value == null || value.Length <= length ? value : value[..length];

        /// <summary>The reason a decision is counted under: an amount or method fill, then a food, a newer source, a kind, order last.</summary>
        public static string Stratum(int reasons) =>
            (reasons & ReprocessReason.MethodFill) != 0 ? "D3"
            : (reasons & ReprocessReason.Amount) != 0 ? "D2"
            : (reasons & ReprocessReason.Food) != 0 ? "D4"
            : (reasons & ReprocessReason.Source) != 0 ? "D7"
            : (reasons & ReprocessReason.Kind) != 0 ? "D5"
            : "D1";

        public async Task<LaneQualifyVerdict> AuditAndEvaluateAsync(string tier, IReadOnlyList<string> faults, CancellationToken cancellationToken = default)
        {
            var reasons = faults.ToList();
            AuditorScore? score;
            string? golden;
            ILaneAuditor auditor;
            LaneSamplePlan plan;
            LanePolicy policy;
            if (tier == ReprocessTier.Order)
            {
                (score, golden, auditor, plan, policy) = (AuditorScore.ExactRule, null, new OrderRuleAuditor(), OrderSample, OrderPolicy);
            }
            else
            {
                (score, golden) = ReprocessJudgeRecord.Evaluate(ReprocessJudgeRecord.Embedded(), ReprocessJudgeRecord.EmbeddedFloor(), _judge.ModelName);
                (auditor, plan, policy) = (new LineJudgeAuditor(_judge), RebuildSample, RebuildPolicy);
                if (!_judge.IsConfigured) reasons.Add("no model host is set for the line judge (Ai:BatchOllamaUrl or Ai:OllamaUrl)");
            }

            if (tier == ReprocessTier.Order || _judge.IsConfigured)
            {
                try
                {
                    var audit = await _frame.AuditAsync(Lane, tier, DateTime.UtcNow.AddDays(-30), plan, auditor, new RuleAuditCheck(), null, cancellationToken);
                    if (audit.Sampled >= 10 && audit.Unreadable * 5 > audit.Sampled)
                        reasons.Add($"the line judge {_judge.ModelName} gave no readable answer on {audit.Unreadable} of {audit.Sampled} sampled recipes");
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException && !cancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning(ex, "Reprocess lane {Tier}: the audit could not run", tier);
                    reasons.Add($"the line judge {_judge.ModelName} could not be reached: {ex.GetType().Name}: {ex.Message}");
                }
            }

            return await _frame.EvaluateAsync(new LaneEvaluateRequest(Lane, tier, Signature(tier), policy, score, golden, reasons), cancellationToken);
        }

        public Task<int> DecidedTodayAsync(string tier, CancellationToken cancellationToken = default)
        {
            var since = DateTime.UtcNow.AddHours(-24);
            return _db.LaneDecisions.AsNoTracking().CountAsync(d => d.Lane == Lane && d.Tier == tier && d.CreatedDate >= since, cancellationToken);
        }

        public async Task<ReprocessLaneStatusModel> StatusAsync(bool enabled, CancellationToken cancellationToken = default)
        {
            var version = RecipeLineBuilder.Version;
            var rows = await _db.ReprocessCandidates.AsNoTracking()
                .GroupBy(c => new { c.Reasons, c.Tier, c.Outcome, c.OutcomeReason, Current = c.RuleVersion == version })
                .Select(g => new { g.Key.Reasons, g.Key.Tier, g.Key.Outcome, g.Key.OutcomeReason, g.Key.Current, Count = g.Count(), Last = g.Max(c => c.DetectedAt) })
                .ToListAsync(cancellationToken);
            bool Waiting(int reasons, string? outcome, bool current) =>
                current && (reasons & ReprocessReason.Rebuildable) != 0 && (outcome == null || outcome == ReprocessOutcome.Shadow);

            var status = new ReprocessLaneStatusModel
            {
                Enabled = enabled,
                RuleVersion = version,
                Read = rows.Sum(r => r.Count),
                ReadWithCurrentRules = rows.Where(r => r.Current).Sum(r => r.Count),
                Waiting = rows.Where(r => Waiting(r.Reasons, r.Outcome, r.Current)).Sum(r => r.Count),
                LastDetectedAt = rows.Count == 0 ? null : rows.Max(r => r.Last),
                JudgeModel = _judge.ModelName,
                JudgeProblem = ReprocessJudgeRecord.Evaluate(ReprocessJudgeRecord.Embedded(), ReprocessJudgeRecord.EmbeddedFloor(), _judge.ModelName).Problem,
            };
            foreach (var tier in new[] { ReprocessTier.Rebuild, ReprocessTier.Order })
                status.WaitingByTier[tier] = rows.Where(r => r.Tier == tier && Waiting(r.Reasons, r.Outcome, r.Current)).Sum(r => r.Count);
            foreach (var (flag, code, label) in ReprocessReason.All)
            {
                var with = rows.Where(r => r.Current && (r.Reasons & flag) != 0).ToList();
                status.ByReason.Add(new ReprocessReasonCount(code, label, with.Sum(r => r.Count), with.Where(r => Waiting(r.Reasons, r.Outcome, r.Current)).Sum(r => r.Count)));
            }
            foreach (var group in rows.Where(r => r.Outcome != null).GroupBy(r => r.Outcome!))
                status.ByOutcome[group.Key] = group.Sum(r => r.Count);
            foreach (var group in rows.Where(r => r.Outcome == ReprocessOutcome.Held).GroupBy(r => r.OutcomeReason ?? string.Empty))
                status.HeldByReason[group.Key] = group.Sum(r => r.Count);

            foreach (var tier in new[] { ReprocessTier.Rebuild, ReprocessTier.Order })
            {
                var last = await _db.AuditLogEntries.AsNoTracking()
                    .Where(a => a.EntityType == BatchEntityType && a.ChangeType == BatchChangeType && a.PropertyName == tier)
                    .OrderByDescending(a => a.Id)
                    .Select(a => a.NewValue)
                    .FirstOrDefaultAsync(cancellationToken);
                if (LaneJournal.Read<ReprocessLaneBatchResult>(last) is { } batch) status.LastBatches.Add(batch);
            }
            return status;
        }

        /// <summary>
        /// The line judge as the frame's auditor of a rebuilt recipe: it reads up to
        /// <see cref="JudgedLinesPerRecipe"/> changed lines of the decision and finds the recipe wrong
        /// when it calls any of them worse, right when it calls none worse, and has no answer when it
        /// answers none readably. A decision with no changed line other than its order is right.
        /// </summary>
        public sealed class LineJudgeAuditor : ILaneAuditor
        {
            private readonly IReprocessLineJudge _judge;

            public LineJudgeAuditor(IReprocessLineJudge judge) => _judge = judge;

            public string Model => _judge.ModelName;

            public async Task<LaneAuditAnswer> AuditAsync(LaneDecisionEntity decision, CancellationToken cancellationToken)
            {
                var signals = LaneJournal.Read<ReprocessSignals>(decision.SignalsJson);
                if (signals == null) return new LaneAuditAnswer(null, "the decision has no signals");
                var lines = signals.Lines.Take(JudgedLinesPerRecipe).ToList();
                if (lines.Count == 0) return new LaneAuditAnswer(false, "only the order changed");

                var cases = lines.Select((l, i) => new LineJudgeCase(
                    i, signals.Recipe, l.Raw, l.Method,
                    l.OldFood == null ? null : new LineJudgeRow(l.OldFood, l.OldQuantity ?? 0, l.OldUnit, l.OldKind),
                    new LineJudgeRow(l.NewFood, l.NewQuantity, l.NewUnit, l.NewKind))).ToList();
                var verdicts = await _judge.JudgeAsync(cases, cancellationToken);
                var readable = verdicts.Where(v => v.Judgement != LineJudgement.Unclear).ToList();
                if (readable.Count == 0) return new LaneAuditAnswer(null, "no readable verdict");
                var worse = readable.Where(v => v.Judgement == LineJudgement.Worse).Select(v => lines[v.Id].Raw).ToList();
                return worse.Count > 0
                    ? new LaneAuditAnswer(true, "worse: " + string.Join(" | ", worse))
                    : new LaneAuditAnswer(false, string.Join(", ", readable.Select(v => v.Judgement.ToString().ToLowerInvariant())));
            }
        }

        /// <summary>
        /// The order tier's auditor, a rule: an order-only decision is wrong when a line changed
        /// anything but its position, or when its rows in their new order do not follow the source's
        /// lines (O1).
        /// </summary>
        public sealed class OrderRuleAuditor : ILaneAuditor
        {
            public string Model => "order-rule";

            public Task<LaneAuditAnswer> AuditAsync(LaneDecisionEntity decision, CancellationToken cancellationToken)
            {
                var signals = LaneJournal.Read<ReprocessSignals>(decision.SignalsJson);
                return Task.FromResult(signals == null
                    ? new LaneAuditAnswer(null, "the decision has no signals")
                    : signals.Lines.Count > 0 ? new LaneAuditAnswer(true, "a line changed more than its position")
                    : OutOfOrder(signals) ? new LaneAuditAnswer(true, "the rows do not follow the source's order")
                    : new LaneAuditAnswer(false, null));
            }
        }

        private static bool OutOfOrder(ReprocessSignals signals) =>
            signals.SourceOrder != null && signals.NewOrder != null && !ReprocessRuleAudit.FollowsSource(signals.SourceOrder, signals.NewOrder);

        /// <summary>
        /// The second check of a flagged rebuild, independent of the judge: the rule audit confirms
        /// the flag when a changed line of the recipe fails A1 to A3, the recipe fails more checks
        /// after the rebuild than before (A4), its kcal per serving is implausible (A6) or an order
        /// restore breaks the source's order (O1).
        /// </summary>
        public sealed class RuleAuditCheck : ILaneAuditCheck
        {
            public string Name => ReprocessRuleAudit.Version;

            public Task<bool?> ConfirmsWrongAsync(LaneDecisionEntity decision, CancellationToken cancellationToken)
            {
                var signals = LaneJournal.Read<ReprocessSignals>(decision.SignalsJson);
                return Task.FromResult<bool?>(signals == null ? null : signals.Worse || signals.Implausible || OutOfOrder(signals) || signals.Lines.Any(l => l.Failures.Count > 0));
            }
        }
    }

    /// <summary>
    /// The reprocess lane's undo for the lane frame: takes back the lane's own rebuilds of a tier
    /// since a time (its batches only, never an operator's), newest first, through the rebuild's
    /// journal, and marks those candidates undone so the lane does not take them again until the
    /// line rules change.
    /// </summary>
    public sealed class ReprocessLaneUndo : ILaneUndo
    {
        private readonly IRecipeReprocessService _reprocess;
        private readonly ApplicationDbContext _db;

        public ReprocessLaneUndo(IRecipeReprocessService reprocess, ApplicationDbContext db)
        {
            _reprocess = reprocess;
            _db = db;
        }

        public string Lane => ReprocessLaneService.Lane;

        public async Task<LaneUndoResult> UndoSinceAsync(string tier, DateTime sinceUtc, bool apply, CancellationToken cancellationToken)
        {
            var kinds = tier == ReprocessTier.Order
                ? new[] { RecipeReprocessKind.Order }
                : new[] { RecipeReprocessKind.RuleReject, RecipeReprocessKind.Rebuild };
            int restored = 0, skipped = 0;
            var recipes = new HashSet<long>();
            foreach (var kind in kinds)
            {
                var result = await _reprocess.UndoAsync(sinceUtc, null, kind, apply, laneOnly: true);
                restored += result.Restored;
                skipped += result.Skipped;
            }
            if (apply)
            {
                var undone = await _db.RecipeReprocessJournals.AsNoTracking()
                    .Where(j => j.UndoneAt != null && j.CreatedDate >= sinceUtc && j.BatchId.StartsWith(RecipeReprocessService.LaneBatchPrefix) && kinds.Contains(j.Kind))
                    .Select(j => j.RecipeId)
                    .Distinct()
                    .ToListAsync(cancellationToken);
                recipes.UnionWith(undone);
                var now = DateTime.UtcNow;
                foreach (var chunk in recipes.Chunk(1000))
                {
                    foreach (var row in await _db.ReprocessCandidates.Where(c => chunk.Contains(c.RecipeId) && c.Outcome == ReprocessOutcome.Rebuilt).ToListAsync(cancellationToken))
                    {
                        row.Outcome = ReprocessOutcome.Undone;
                        row.OutcomeAt = now;
                        row.LastModifiedDate = now;
                        row.LastModifiedByPersonId = SystemConstants.SystemPersonId;
                    }
                }
                await _db.SaveChangesAsync(cancellationToken);
            }
            return new LaneUndoResult(restored, skipped, 0, $"reprocess {tier}: rebuilds of the lane's own batches since {sinceUtc:u}");
        }
    }
}

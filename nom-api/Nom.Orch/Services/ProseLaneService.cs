using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nom.Data;
using Nom.Data.Curation;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Curation;
using Nom.Orch.Services.Support;

namespace Nom.Orch.Services
{
    /// <summary>
    /// The reword prose lane (prose rewrite v3) on the lane frame. A sweep takes the recipes whose
    /// prose is withheld or quarantined and the shown recipes with copied sentences on their work list
    /// (approved withheld first, then other withheld, then shown recipes with copied sentences, then
    /// quarantined imports) and runs each through <see cref="ProseReword"/>: the writer reads the whole
    /// method, the step and the sentence before it rewords the sentence, every reworded sentence is
    /// checked for its facts and for copying and reworded again up to the last round, then the prose
    /// check and the judge read the whole. A withheld or quarantined recipe is reworded from its source
    /// method as a whole; a shown one only in its copied sentences. A withheld recipe whose method a person wrote keeps
    /// it, and the live lane replaces only its copied description; a description a person wrote is
    /// kept. Every outcome is a decision in curation.LaneDecision; a rewrite is written, journaled
    /// (<see cref="ProseLaneJournal"/>) and lifts the withhold or quarantine only while the frame
    /// has the lane live. A recipe every attempt failed is marked as given up under the lane
    /// version, with its failure class, and becomes eligible again when the version changes.
    /// The lane measures itself: planted-fault canaries from prose-judge.tsv after every
    /// <see cref="CanaryEvery"/> rewrites, an auditor of a third family on a sample, its gate yield,
    /// and its golden records; the frame qualifies, pauses and undoes it on those figures.
    /// A recipe whose source reserves its content against AI use (<see cref="RecipeEntity.SourceAiReserved"/>)
    /// takes the model-free path only (<see cref="ProseCleanRoom.RunRuleOnly"/>): no extractor, writer,
    /// judge or auditor reads its method. Its decisions are recorded in <see cref="RuleOnlyTier"/>, a tier
    /// of its own on the frame: it writes only while that tier is live, every one of its decisions is
    /// audited by rule (<see cref="ProseRuleOnlyAudit"/>), and the frame qualifies, pauses and undoes it on
    /// that audit alone, whatever the model tier's mode.
    /// A facts-only recipe (<see cref="RecipeEntity.SourceRights"/>) never takes the description-only path
    /// for a method a person edited: its whole method is rewritten clean-room, so no untouched publisher
    /// step is shown again.
    /// </summary>
    public class ProseLaneService
    {
        public const string Lane = AiLanes.ProseRewrite;
        public const string Tier = LaneTiers.Whole;
        public const string CanaryTier = "canary";
        public const string RuleOnlyTier = "rule-only";
        public const string RewriteDecision = "rewrite";
        public const string GaveUpPrefix = "exhausted:";
        public const int CanaryEvery = 100;
        public const int CanaryFaults = 4;
        public const int YieldWindow = 200;
        public const double MinimumYield = 0.5;
        public const double LiveAuditRate = 0.05;
        public const double ShadowAuditRate = 0.25;
        public const int AuditMinimum = 30;

        /// <summary>The lane's bars on the frame: 96 % lower bound to go live, pause above 5 % confirmed errors over a window of 100 audited rewrites.</summary>
        public static readonly LanePolicy Policy = new(QualifyBar: 0.96, PauseBar: 0.05, PauseWindow: 100, MinimumAudited: 100);

        private static readonly DateTime Epoch = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private readonly ApplicationDbContext _db;
        private readonly ILaneFrameService _frame;
        private readonly IProseStockService _stock;
        private readonly ILogger<ProseLaneService> _logger;
        private readonly ProseLaneGolden _golden;

        public ProseLaneService(ApplicationDbContext db, ILaneFrameService frame, IProseStockService stock, ILogger<ProseLaneService> logger)
            : this(db, frame, stock, logger, ProseLaneGolden.Default)
        {
        }

        public ProseLaneService(ApplicationDbContext db, ILaneFrameService frame, IProseStockService stock, ILogger<ProseLaneService> logger, ProseLaneGolden golden)
        {
            _db = db;
            _frame = frame;
            _stock = stock;
            _logger = logger;
            _golden = golden;
        }

        private sealed record Source(RewriteFidelity.SourceText Text, IReadOnlyList<string> Lines);

        /// <summary>
        /// One sweep of up to <paramref name="batchSize"/> recipes in <paramref name="mode"/>. A model
        /// host outage ends the sweep at the recipe it hit, which is not charged an attempt; the
        /// exception is returned for the lane gate.
        /// </summary>
        public async Task<ProseSweepResult> SweepAsync(string mode, ProseLaneModels models, IProseRewriter writer, IProseJudge judge, int batchSize, CancellationToken cancellationToken = default)
        {
            var version = models.Version;
            var result = new ProseSweepResult { Mode = mode, LaneVersion = version };
            var stock = await _stock.LoadAsync(cancellationToken);
            var live = mode == LaneMode.Live;
            var ruleMode = await _frame.ModeAsync(Lane, RuleOnlyTier, cancellationToken);
            var ids = await EligibleQuery(_db, version, live, ruleMode == LaneMode.Live)
                .OrderBy(r => r.ProseWithheld ? (r.CurationStatusId == (long)CurationStatusEnum.Curated ? 0 : 1) : r.ContainsSourceProse ? 3 : 2)
                .ThenBy(r => r.ProseRewriteAttemptedAt == null ? 0 : 1)
                .ThenBy(r => r.ProseRewriteAttemptedAt)
                .ThenBy(r => r.Id)
                .Select(r => r.Id)
                .Take(Math.Clamp(batchSize, 1, 500))
                .ToListAsync(cancellationToken);

            foreach (var id in ids)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var recipe = await _db.Recipes.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
                if (recipe == null || (!recipe.ProseWithheld && !recipe.ContainsSourceProse && recipe.ProseCopiedSentences == 0)) continue;
                var repair = recipe.ProseCopiedSentences > 0 && !recipe.ContainsSourceProse && !recipe.SourceAiReserved && recipe.SourceRights != SourceRightsRule.FactsOnly;
                result.Seen++;
                var rows = await _db.RecipeSteps.IgnoreQueryFilters().Where(s => s.RecipeId == id).ToListAsync(cancellationToken);
                var steps = rows.Where(s => !s.IsDeleted).OrderBy(s => s.StepNumber).ToList();
                var source = await SourceAsync(recipe, steps, cancellationToken);
                var ownDescription = await PersonDescriptionAsync(recipe, cancellationToken);
                var recipeMode = recipe.SourceAiReserved ? ruleMode : mode;
                if (steps.Any(EditedByPerson) && (repair || recipe.SourceRights != SourceRightsRule.FactsOnly))
                {
                    if (!live || recipe.ContainsSourceProse || !recipe.ProseWithheld) continue;
                    var description = ownDescription ?? ProseDescription.Build(recipe.Name, ProseMasking.Mask(Array.Empty<string>(), source.Lines).Lines);
                    WriteDescription(recipe, steps, description, models, DateTime.UtcNow);
                    await _db.SaveChangesAsync(cancellationToken);
                    _db.ChangeTracker.Clear();
                    result.DescriptionOnly++;
                    continue;
                }

                var input = new ProseLaneInput(recipe.Name ?? string.Empty, source.Text, source.Lines, stock);

                ProseLaneOutcome outcome;
                try
                {
                    outcome = recipe.SourceAiReserved
                        ? ProseCleanRoom.RunRuleOnly(input)
                        : await ProseReword.RunAsync(input, repair ? new RewriteFidelity.SourceText(recipe.Description ?? string.Empty, steps.Select(st => st.Description).ToList(), recipe.Name ?? string.Empty) : null,
                            writer, judge, cancellationToken);
                }
                catch (System.Net.Http.HttpRequestException ex) when (!AiLanes.IsOutage(ex))
                {
                    _logger.LogWarning(ex, "Prose lane: the model host refused recipe {Id} ({Status}); it goes to the back of the queue", id, ex.StatusCode);
                    recipe.ProseRewriteAttemptedAt = DateTime.UtcNow;
                    await _db.SaveChangesAsync(cancellationToken);
                    _db.ChangeTracker.Clear();
                    result.Errors++;
                    continue;
                }
                catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
                {
                    _db.ChangeTracker.Clear();
                    result.Outage = ex;
                    _logger.LogWarning(ex, "Prose lane: the model host failed while rewriting recipe {Id}; ending the sweep without charging it", id);
                    break;
                }
                catch (Exception ex) when (ex is not OperationCanceledException and not System.Net.Http.HttpRequestException)
                {
                    _logger.LogWarning(ex, "Prose lane: rewriting recipe {Id} failed ({Error}); it goes to the back of the queue", id, ex.Message);
                    recipe.ProseRewriteAttemptedAt = DateTime.UtcNow;
                    await _db.SaveChangesAsync(cancellationToken);
                    _db.ChangeTracker.Clear();
                    result.Errors++;
                    continue;
                }

                var now = DateTime.UtcNow;
                var stratum = repair ? "repair" : recipe.ProseWithheld ? (recipe.CurationStatusId == (long)CurationStatusEnum.Curated ? "withheld-approved" : "withheld") : "quarantined";
                var tier = recipe.SourceAiReserved ? RuleOnlyTier : Tier;
                if (outcome.Passed)
                {
                    if (recipeMode == LaneMode.Live)
                    {
                        Write(recipe, rows, steps, outcome.Rewrite!, ownDescription, models, now, recipe.SourceAiReserved ? ProseCleanRoom.RuleOnlyStyle : null);
                        result.Rewritten++;
                    }
                    else
                    {
                        recipe.ProseLaneVersion = version;
                        recipe.ProseAttempts = 0;
                        recipe.ProseRewriteAttemptedAt = now;
                        result.WouldRewrite++;
                    }
                }
                else
                {
                    recipe.ProseLaneVersion = version;
                    recipe.ProseAttempts = ProseCleanRoom.AttemptCount;
                    recipe.ProseRewriteAttemptedAt = now;
                    result.GaveUp++;
                    var failure = outcome.FailureClass ?? "unknown";
                    result.GaveUpByClass[failure] = result.GaveUpByClass.GetValueOrDefault(failure) + 1;
                }

                await _db.SaveChangesAsync(cancellationToken);
                _db.ChangeTracker.Clear();
                await _frame.RecordAsync(new LaneDecisionInput(
                    Lane, tier, Subject(id),
                    outcome.Passed ? RewriteDecision : GaveUpPrefix + (outcome.FailureClass ?? "unknown"),
                    recipeMode, outcome.Passed, version, stratum,
                    new
                    {
                        recipeId = id,
                        attempts = outcome.Attempts.Select(a => new { a.Number, a.Style, a.Reasons }),
                        ruleSheets = outcome.RuleSheets,
                        rewrite = outcome.Rewrite == null ? null : new { description = outcome.Rewrite.Description, steps = outcome.Rewrite.Steps },
                        copiedShare = outcome.Finding?.CopiedShare,
                        longestRun = outcome.Finding?.LongestRun,
                        lengthRatio = outcome.Finding?.LengthRatio,
                        stockVersion = stock.Version,
                    },
                    outcome.Verdict?.Steps), cancellationToken);
            }

            _logger.LogInformation("Prose lane sweep ({Mode}): {Seen} recipes, {Rewritten} rewritten, {DescriptionOnly} description only, {Would} would be rewritten, {GaveUp} given up {Classes}",
                mode, result.Seen, result.Rewritten, result.DescriptionOnly, result.WouldRewrite, result.GaveUp,
                string.Join(", ", result.GaveUpByClass.Select(kv => $"{kv.Key} {kv.Value}")));
            return result;
        }

        /// <summary>
        /// The recipes the lane may take under <paramref name="version"/>: withheld or quarantined, or
        /// marked with copied sentences (never an AI-reserved one, which no model may read), and not given
        /// up under this version. A quarantined recipe with a step a person edited is never
        /// taken, unless it is a withheld facts-only recipe. While the lane is not <paramref name="live"/>,
        /// a recipe already decided under this version is not taken again, so every decision it records
        /// is of a different recipe, and a withheld recipe whose method a person wrote waits for the live
        /// lane, which only replaces its copied description. An AI-reserved recipe follows the rule-only
        /// tier's mode (<paramref name="ruleLive"/>) instead of the lane's.
        /// </summary>
        public static IQueryable<RecipeEntity> EligibleQuery(ApplicationDbContext db, string version, bool live = true, bool ruleLive = false) =>
            db.Recipes.AsNoTracking()
                .Where(r => r.ProseWithheld || r.ContainsSourceProse || (r.ProseCopiedSentences > 0 && !r.SourceAiReserved))
                .Where(r => !(r.ProseLaneVersion == version && r.ProseAttempts >= ProseCleanRoom.AttemptCount))
                .Where(r => (r.SourceAiReserved ? ruleLive : live) || r.ProseLaneVersion != version)
                .Where(r => !r.RecipeSteps!.Any(s => s.LastModifiedByPersonId != null && s.LastModifiedByPersonId != SystemConstants.SystemPersonId)
                    || (live && r.ProseWithheld && !r.ContainsSourceProse)
                    || (r.ProseWithheld && r.SourceRights == SourceRightsRule.FactsOnly));

        /// <summary>The recipes the lane gave up on under <paramref name="version"/> and that are still withheld or quarantined.</summary>
        public static IQueryable<RecipeEntity> GaveUpQuery(ApplicationDbContext db, string version) =>
            db.Recipes.AsNoTracking()
                .Where(r => (r.ProseWithheld || r.ContainsSourceProse || r.ProseCopiedSentences > 0) && r.ProseLaneVersion == version && r.ProseAttempts >= ProseCleanRoom.AttemptCount);

        public static string Subject(long recipeId) => "recipe:" + recipeId.ToString(CultureInfo.InvariantCulture);

        /// <summary>Whether a canary run is due: none has run under this version, or <see cref="CanaryEvery"/> rewrites have been decided since the last.</summary>
        public async Task<bool> CanariesDueAsync(string version, CancellationToken cancellationToken = default)
        {
            var last = await _db.LaneDecisions.AsNoTracking()
                .Where(d => d.Lane == Lane && d.Tier == CanaryTier && d.SignatureHash == version)
                .MaxAsync(d => (DateTime?)d.CreatedDate, cancellationToken);
            if (last == null) return true;
            var since = await _db.LaneDecisions.AsNoTracking()
                .CountAsync(d => d.Lane == Lane && d.Tier == Tier && d.Writes && d.SignatureHash == version && d.CreatedDate > last, cancellationToken);
            return since >= CanaryEvery;
        }

        /// <summary>
        /// Puts <see cref="CanaryFaults"/> planted-fault rewrites of prose-judge.tsv (those only the
        /// judge can catch first) and one faithful rewrite through the lane's gate, check v2 then the
        /// judge, and records each as a decision of the canary tier. A planted fault the gate lets
        /// through is a false accept, which pauses the lane until a later canary run is clean; a
        /// refused faithful rewrite is recorded, not a fault.
        /// </summary>
        public async Task<ProseCanaryResult> RunCanariesAsync(ProseLaneModels models, IProseJudge judge, CancellationToken cancellationToken = default)
        {
            var version = models.Version;
            var stock = await _stock.LoadAsync(cancellationToken);
            var round = await _db.LaneDecisions.AsNoTracking().CountAsync(d => d.Lane == Lane && d.Tier == CanaryTier, cancellationToken) / (CanaryFaults + 1);
            var faults = _golden.Cases.Where(c => !c.Faithful).OrderBy(c => c.Check2 == "passed" ? 0 : 1).ThenBy(c => c.Id, StringComparer.Ordinal).ToList();
            var faithful = _golden.Cases.Where(c => c.Faithful).OrderBy(c => c.Id, StringComparer.Ordinal).ToList();
            var judgeOnly = faults.Count(c => c.Check2 == "passed");
            var pool = judgeOnly >= CanaryFaults ? faults.Take(judgeOnly).ToList() : faults;
            var picked = Enumerable.Range(0, Math.Min(CanaryFaults, pool.Count)).Select(i => pool[(round * CanaryFaults + i) % pool.Count]).ToList();
            if (faithful.Count > 0) picked.Add(faithful[round % faithful.Count]);

            var result = new ProseCanaryResult { LaneVersion = version };
            foreach (var canary in picked)
            {
                var reasons = await ProseCleanRoom.GateAsync(canary.Input(stock), canary.RewriteText, judge, cancellationToken);
                var accepted = reasons.Count == 0;
                var outcome = canary.Faithful ? (accepted ? "accepted" : "false-reject") : (accepted ? "false-accept" : "refused");
                if (outcome == "false-accept") result.FalseAccepts.Add(canary.Id);
                if (outcome == "false-reject") result.FalseRejects.Add(canary.Id);
                result.Run++;
                await _frame.RecordAsync(new LaneDecisionInput(Lane, CanaryTier, "canary:" + canary.Id, outcome, LaneMode.Shadow, false, version,
                    canary.Faithful ? "faithful" : "fault", new { canary = canary.Id, fault = canary.Fault, reasons }), cancellationToken);
            }

            if (result.FalseAccepts.Count > 0)
                _logger.LogWarning("Prose lane canaries: the gate let planted faults through ({Ids}); the lane pauses", string.Join(", ", result.FalseAccepts));
            return result;
        }

        /// <summary>
        /// The faults that pause the lane or hold it in shadow now: a planted fault let through in the
        /// last canary run, and a gate yield (rewrites among recipes decided) under
        /// <see cref="MinimumYield"/> over the last <see cref="YieldWindow"/> recipes.
        /// </summary>
        public async Task<IReadOnlyList<string>> FaultsAsync(string version, CancellationToken cancellationToken = default)
        {
            var faults = new List<string>();
            var canaries = await _db.LaneDecisions.AsNoTracking()
                .Where(d => d.Lane == Lane && d.Tier == CanaryTier && d.SignatureHash == version)
                .OrderByDescending(d => d.Id)
                .Take(CanaryFaults + 1)
                .Select(d => new { d.Subject, d.Decision })
                .ToListAsync(cancellationToken);
            var falseAccepts = canaries.Where(c => c.Decision == "false-accept").Select(c => c.Subject).ToList();
            if (falseAccepts.Count > 0) faults.Add($"canary false accept: {string.Join(", ", falseAccepts)}");

            var recent = await _db.LaneDecisions.AsNoTracking()
                .Where(d => d.Lane == Lane && d.Tier == Tier && d.SignatureHash == version)
                .OrderByDescending(d => d.Id)
                .Take(YieldWindow)
                .Select(d => d.Writes)
                .ToListAsync(cancellationToken);
            if (recent.Count >= YieldWindow)
            {
                var yield = (double)recent.Count(w => w) / recent.Count;
                if (yield < MinimumYield)
                    faults.Add(string.Create(CultureInfo.InvariantCulture, $"gate yield {yield:P0} over the last {YieldWindow} recipes, under {MinimumYield:P0}"));
            }

            return faults;
        }

        /// <summary>
        /// Audits a sample of the lane's rewrites when enough are waiting (a quarter while it is in
        /// shadow or on probation, a twentieth while live, at least <see cref="AuditMinimum"/>), then
        /// evaluates it on the frame with its bars, the audit check's golden score, its golden
        /// records and its faults.
        /// </summary>
        public async Task<LaneQualifyVerdict> MeasureAsync(string mode, ProseLaneModels models, IProseJudge auditor, Random? random = null, CancellationToken cancellationToken = default)
        {
            var version = models.Version;
            var rate = mode == LaneMode.Live ? LiveAuditRate : ShadowAuditRate;
            var waiting = await _db.LaneDecisions.AsNoTracking()
                .CountAsync(d => d.Lane == Lane && d.Tier == Tier && d.Writes && d.AuditOutcome == null && d.SignatureHash == version, cancellationToken);
            if (waiting >= (int)Math.Ceiling(AuditMinimum / rate))
            {
                var audit = new ProseLaneAudit(_db, auditor);
                await _frame.AuditAsync(Lane, Tier, Epoch, new LaneSamplePlan(AuditMinimum, rate), audit, audit, random, cancellationToken);
            }

            var faults = await FaultsAsync(version, cancellationToken);
            await MeasureRuleOnlyAsync(version, cancellationToken);
            return await _frame.EvaluateAsync(new LaneEvaluateRequest(Lane, Tier, version, Policy, _golden.AuditorScore(models), _golden.GoldenProblem(models), faults), cancellationToken);
        }

        private static readonly LaneSamplePlan RuleOnlyAuditPlan = new(Minimum: int.MaxValue, Rate: 1.0);

        /// <summary>
        /// Audits every unaudited rule-only rendering by rule (<see cref="ProseRuleOnlyAudit"/>: no model
        /// reads the source or the rendering) and evaluates the rule-only tier on that audit with the
        /// lane's bars, so it qualifies from shadow, pauses on its own observed error and has its writes
        /// undone, apart from the model tier.
        /// </summary>
        public async Task<LaneQualifyVerdict> MeasureRuleOnlyAsync(string version, CancellationToken cancellationToken = default)
        {
            var audit = new ProseRuleOnlyAudit(_db);
            await _frame.AuditAsync(Lane, RuleOnlyTier, Epoch, RuleOnlyAuditPlan, audit, audit, null, cancellationToken);
            return await _frame.EvaluateAsync(new LaneEvaluateRequest(Lane, RuleOnlyTier, version, Policy, AuditorScore.ExactRule), cancellationToken);
        }

        private async Task<Source> SourceAsync(RecipeEntity recipe, List<RecipeStepEntity> steps, CancellationToken cancellationToken)
        {
            var raw = await _db.ScrapedDocuments.AsNoTracking()
                .Where(d => d.RecipeId == recipe.Id && d.Format == ScrapedDocumentFormat.JsonLd)
                .OrderByDescending(d => d.Id)
                .Select(d => d.RawJsonLd)
                .FirstOrDefaultAsync(cancellationToken);
            var lines = ProseCheck.SourceLines(raw);
            if (lines.Count == 0)
            {
                lines = await _db.RecipeIngredients.AsNoTracking()
                    .Where(i => i.RecipeId == recipe.Id && i.RawLine != "")
                    .OrderBy(i => i.SortOrder)
                    .Select(i => i.RawLine)
                    .ToListAsync(cancellationToken);
            }

            var text = RewriteFidelity.Source(raw)
                ?? new RewriteFidelity.SourceText(recipe.Description ?? string.Empty, steps.Select(s => s.Description).ToList(), recipe.Name ?? string.Empty);
            return new Source(text, lines);
        }

        private static bool EditedByPerson(RecipeStepEntity step) =>
            step.LastModifiedByPersonId is { } by && by != SystemConstants.SystemPersonId;

        /// <summary>
        /// The recipe's description when a person wrote it while its prose was withheld or
        /// quarantined (journaled as <see cref="ProseRecheckService.DescriptionByPersonChangeType"/>
        /// by the recipe update, and unchanged since); null otherwise.
        /// </summary>
        private async Task<string?> PersonDescriptionAsync(RecipeEntity recipe, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(recipe.Description)) return null;
            var written = await _db.AuditLogEntries.AsNoTracking()
                .Where(a => a.EntityType == ProseRecheckService.RecipeEntityType && a.EntityId == recipe.Id && a.ChangeType == ProseRecheckService.DescriptionByPersonChangeType)
                .OrderByDescending(a => a.Id)
                .Select(a => a.NewValue)
                .FirstOrDefaultAsync(cancellationToken);
            return written != null && written == ProseRecheckService.Clamp(recipe.Description) ? recipe.Description : null;
        }

        private void WriteDescription(RecipeEntity recipe, List<RecipeStepEntity> steps, string description, ProseLaneModels models, DateTime now)
        {
            var tag = ProseLaneJournal.Tag(models.Writer, models.Version);
            var before = ProseState.Of(recipe, steps);
            var after = new ProseState(false, false, recipe.ProseCheckVersion, models.Version, 0, now, before.StepNumbers);
            _db.AuditLogEntries.AddRange(ProseLaneJournal.Rows(recipe.Id, ProseLaneJournal.DescriptionProperty, recipe.Description, $"{tag} {description}", now));
            _db.AuditLogEntries.AddRange(ProseLaneJournal.Rows(recipe.Id, ProseLaneJournal.StateProperty, ProseLaneJournal.StateJson(before), $"{tag} {ProseLaneJournal.StateJson(after)}", now));
            recipe.Description = description;
            after.ApplyTo(recipe);
            recipe.ProseCopiedSentences = 0;
            recipe.LastModifiedDate = now;
            recipe.LastModifiedByPersonId = SystemConstants.SystemPersonId;
        }

        private void Write(RecipeEntity recipe, List<RecipeStepEntity> rows, List<RecipeStepEntity> steps, RewriteFidelity.SourceText rewrite, string? ownDescription, ProseLaneModels models, DateTime now, string? writer = null)
        {
            var description = ownDescription ?? rewrite.Description;
            var tag = ProseLaneJournal.Tag(writer ?? models.Writer, models.Version);
            var texts = rewrite.Steps.Select((text, i) => (Number: i + 1, Text: text)).ToDictionary(s => s.Number, s => s.Text);
            var before = ProseState.Of(recipe, steps);
            var after = new ProseState(false, false, ProseCheck.Version, models.Version, 0, now, texts.Keys.ToList());

            _db.AuditLogEntries.AddRange(ProseLaneJournal.Rows(recipe.Id, ProseLaneJournal.DescriptionProperty, recipe.Description, $"{tag} {description}", now));
            foreach (var number in steps.Select(s => s.StepNumber).Union(texts.Keys).OrderBy(n => n))
            {
                var old = steps.FirstOrDefault(s => s.StepNumber == number)?.Description;
                var next = texts.TryGetValue(number, out var text) ? text : "(removed)";
                _db.AuditLogEntries.AddRange(ProseLaneJournal.Rows(recipe.Id, ProseLaneJournal.StepPrefix + number.ToString(CultureInfo.InvariantCulture), old, $"{tag} {next}", now));
            }

            _db.AuditLogEntries.AddRange(ProseLaneJournal.Rows(recipe.Id, ProseLaneJournal.StateProperty, ProseLaneJournal.StateJson(before), $"{tag} {ProseLaneJournal.StateJson(after)}", now));

            recipe.Description = description;
            ProseLaneJournal.SetSteps(_db, recipe.Id, rows, texts, now);
            after.ApplyTo(recipe);
            recipe.ProseCopiedSentences = 0;
            recipe.LastModifiedDate = now;
            recipe.LastModifiedByPersonId = SystemConstants.SystemPersonId;
        }
    }

    /// <summary>
    /// The audit of the rule-only tier's renderings, by rule: each written method is read back from its
    /// text alone (<see cref="ProseCleanRoom.RuleOnlyAudit"/>) against the recipe's stored source steps and
    /// lines, so a rendering that loses a step, an ingredient, an amount or a cooking verb is an error.
    /// No model reads either text. Being exact, it is its own confirmation.
    /// </summary>
    public sealed class ProseRuleOnlyAudit : ILaneAuditor, ILaneAuditCheck
    {
        private readonly ApplicationDbContext _db;

        public ProseRuleOnlyAudit(ApplicationDbContext db) => _db = db;

        public string Model => ProseCleanRoom.RuleOnlyStyle;

        public string Name => ProseCleanRoom.RuleOnlyStyle + " read-back";

        public async Task<LaneAuditAnswer> AuditAsync(LaneDecisionEntity decision, CancellationToken cancellationToken)
        {
            var problems = await ProblemsAsync(decision, cancellationToken);
            return problems == null ? new LaneAuditAnswer(null, "no source or rendering to read")
                : problems.Count == 0 ? new LaneAuditAnswer(false)
                : new LaneAuditAnswer(true, string.Join("; ", problems.Take(3)));
        }

        public async Task<bool?> ConfirmsWrongAsync(LaneDecisionEntity decision, CancellationToken cancellationToken) =>
            await ProblemsAsync(decision, cancellationToken) is { } problems ? problems.Count > 0 : null;

        private async Task<IReadOnlyList<string>?> ProblemsAsync(LaneDecisionEntity decision, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(decision.SignalsJson)) return null;
            using var signals = JsonDocument.Parse(decision.SignalsJson);
            var root = signals.RootElement;
            if (!root.TryGetProperty("recipeId", out var idElement) || !idElement.TryGetInt64(out var recipeId)) return null;
            if (!root.TryGetProperty("rewrite", out var rewrite) || rewrite.ValueKind != JsonValueKind.Object || !rewrite.TryGetProperty("steps", out var steps)) return null;
            var written = steps.EnumerateArray().Select(s => s.GetString() ?? string.Empty).ToList();
            var raw = await _db.ScrapedDocuments.AsNoTracking()
                .Where(d => d.RecipeId == recipeId && d.Format == ScrapedDocumentFormat.JsonLd)
                .OrderByDescending(d => d.Id)
                .Select(d => d.RawJsonLd)
                .FirstOrDefaultAsync(cancellationToken);
            var source = RewriteFidelity.Source(raw);
            if (source == null) return null;
            var lines = ProseCheck.SourceLines(raw);
            if (lines.Count == 0)
            {
                lines = await _db.RecipeIngredients.AsNoTracking()
                    .Where(i => i.RecipeId == recipeId && i.RawLine != "")
                    .OrderBy(i => i.SortOrder)
                    .Select(i => i.RawLine)
                    .ToListAsync(cancellationToken);
            }

            return ProseCleanRoom.RuleOnlyAudit(source.Steps, written, lines);
        }
    }

    /// <summary>
    /// The audit of the prose lane's rewrites: the auditor (a model of neither the writer's nor
    /// the judge's family) reads a sampled rewrite against its source with the judge's rubric, and a
    /// flag counts as an error only when the auditor, asked again blind about the flagged step,
    /// agrees. The judge never confirms: it already passed every rewrite in the sample, so its
    /// confirmation would only repeat its own verdict and the golden score of the check (measured
    /// on rows no judge had filtered) would overstate how much of the error it finds.
    /// </summary>
    public sealed class ProseLaneAudit : ILaneAuditor, ILaneAuditCheck
    {
        private readonly ApplicationDbContext _db;
        private readonly IProseJudge _auditor;
        private readonly Dictionary<long, int> _flagged = new();

        public ProseLaneAudit(ApplicationDbContext db, IProseJudge auditor)
        {
            _db = db;
            _auditor = auditor;
        }

        public string Model => _auditor.Model;

        public string Name => $"{_auditor.Model} re-reading the flagged step blind";

        public async Task<LaneAuditAnswer> AuditAsync(LaneDecisionEntity decision, CancellationToken cancellationToken)
        {
            var pair = await PairAsync(decision, cancellationToken);
            if (pair == null) return new LaneAuditAnswer(null, "no source or rewrite to read");
            var verdict = await _auditor.JudgeAsync(pair.Value.Original, pair.Value.Rewrite, cancellationToken);
            if (verdict == null) return new LaneAuditAnswer(null, "unreadable answer");
            if (verdict.Faithful) return new LaneAuditAnswer(false);
            var step = verdict.FirstFlagged?.Number ?? 1;
            _flagged[decision.Id] = step;
            return new LaneAuditAnswer(true, $"step {step}: {string.Join(", ", verdict.FirstFlagged?.Faults() ?? Enumerable.Empty<string>())}");
        }

        public async Task<bool?> ConfirmsWrongAsync(LaneDecisionEntity decision, CancellationToken cancellationToken)
        {
            var pair = await PairAsync(decision, cancellationToken);
            if (pair == null) return null;
            return await _auditor.ConfirmAsync(pair.Value.Original, pair.Value.Rewrite, _flagged.GetValueOrDefault(decision.Id, 1), cancellationToken);
        }

        private async Task<(IReadOnlyList<string> Original, IReadOnlyList<string> Rewrite)?> PairAsync(LaneDecisionEntity decision, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(decision.SignalsJson)) return null;
            using var signals = JsonDocument.Parse(decision.SignalsJson);
            var root = signals.RootElement;
            if (!root.TryGetProperty("recipeId", out var idElement) || !idElement.TryGetInt64(out var recipeId)) return null;
            if (!root.TryGetProperty("rewrite", out var rewrite) || rewrite.ValueKind != JsonValueKind.Object || !rewrite.TryGetProperty("steps", out var steps)) return null;
            var written = steps.EnumerateArray().Select(s => s.GetString() ?? string.Empty).ToList();
            var raw = await _db.ScrapedDocuments.AsNoTracking()
                .Where(d => d.RecipeId == recipeId && d.Format == ScrapedDocumentFormat.JsonLd)
                .OrderByDescending(d => d.Id)
                .Select(d => d.RawJsonLd)
                .FirstOrDefaultAsync(cancellationToken);
            var source = RewriteFidelity.Source(raw);
            if (source == null) return null;
            return (source.Steps.Select(ProseQuantities.Prepare).Where(s => s.Length > 0).ToList(), written);
        }
    }
}

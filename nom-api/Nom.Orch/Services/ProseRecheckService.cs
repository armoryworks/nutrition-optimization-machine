using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Nom.Data;
using Nom.Data.Audit;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Curation;
using Nom.Orch.Services.Support;

namespace Nom.Orch.Services
{
    /// <summary>
    /// Withholds, by origin, the prose of every web recipe whose shown method is an earlier paraphrase of its
    /// stored source (<see cref="RecipeEntity.ProseWithheld"/>): a scraped recipe of no clear licence
    /// (<see cref="IsClearLicence"/>), with a stored JSON-LD source, whose steps no person wrote or edited and
    /// whose text the reword lane did not write (<see cref="ProseLanePrompts.LaneVersionPrefix"/>). No word-order
    /// rule can sort the earlier paraphrases (the proof of 2026-11-24), so none is shown until the lane has
    /// reworded its whole method; the recipe stays listed and usable with rule-built key facts and the source
    /// link, and the rewrite lane takes it up. <see cref="ProseCheck"/> still reads every earlier paraphrase,
    /// shown or already withheld, but its verdict (mostly, some or no sentences copied) only orders the lane's
    /// queue: the count of sentences it reads as copied or kept nearly word for word is stored as
    /// <see cref="RecipeEntity.ProseCopiedSentences"/> (journaled as <see cref="RepairChangeType"/>), and the
    /// apply withholds approved recipes first, then the most copied (the figures of recipes already withheld are set
    /// only by an apply that withholds every earlier paraphrase still shown). Each recipe withheld is journaled
    /// (<see cref="WithholdChangeType"/>) with its verdict and the check version it had, in batches of
    /// <see cref="WithholdBatch"/>, and the undo releases it and puts its figure back. A withhold clears an
    /// unsure mark (<see cref="RecipeEntity.ProseUnsure"/>, journaled as <see cref="UnsureChangeType"/>), as does
    /// a person's edit. A full run is recorded (<see cref="RunChangeType"/>) so the weekly job knows which check
    /// and stock-index versions have already been run.
    /// </summary>
    public class ProseRecheckService : IProseRecheckService
    {
        public const string RecipeEntityType = "Recipe";
        public const string RewriteChangeType = "ProseRewrite";
        public const string RecheckChangeType = "ProseRecheck";
        public const string WithholdChangeType = "ProseWithhold";
        public const string WithholdUndoChangeType = "ProseWithholdUndo";
        public const string WithholdEndChangeType = "ProseWithholdEnd";
        public const string UnsureChangeType = "ProseUnsure";
        public const string RepairChangeType = "ProseCopiedSentences";
        public const string DescriptionByPersonChangeType = "ProseDescriptionByPerson";
        public const string RunEntityType = "ProseRecheck";
        public const string RunChangeType = "ProseRecheckRun";
        public const string CheckErrorReason = "check-error";
        public const string Mostly = "mostly";
        public const string Some = "some";
        public const string None = "none";
        public const int JournalLength = 4000;
        public const int WithholdBatch = 500;
        public const int MaxListedRecipeIds = 1000;
        public const string NotCheckedEdited = "not checked, steps edited by a person";
        public const string NotCheckedWithholdEnded = "not checked, withhold ended by a person";

        private const int RecipesPerRead = 500;
        private const int RecipesPerTransaction = 200;

        private static readonly Regex RunVersions = new(@"^check (?<check>\d+) stock (?<stock>\d+)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private readonly ApplicationDbContext _db;
        private readonly ICurrentUserService _currentUser;
        private readonly IProseStockService _stock;

        public ProseRecheckService(ApplicationDbContext db, ICurrentUserService currentUser, IProseStockService stock)
        {
            _db = db;
            _currentUser = currentUser;
            _stock = stock;
        }

        internal Action<long>? BeforeCheck { get; set; }

        /// <summary>Whether a recipe's licence is clear: public domain or an open licence. Every other scraped recipe's text is an earlier paraphrase of a source NOM may not show.</summary>
        public static bool IsClearLicence(string? licence) => licence is RecipeLicenseStatus.PublicDomain or RecipeLicenseStatus.CreativeCommons or RecipeLicenseStatus.UserSubmitted;

        /// <summary>The scraped recipes whose licence is not clear (<see cref="IsClearLicence"/>), as a query.</summary>
        public static IQueryable<RecipeEntity> NotClear(IQueryable<RecipeEntity> recipes) =>
            recipes.Where(r => r.ScrapedAtUtc != null && (r.LicenseStatus == null
                || (r.LicenseStatus != RecipeLicenseStatus.PublicDomain && r.LicenseStatus != RecipeLicenseStatus.CreativeCommons && r.LicenseStatus != RecipeLicenseStatus.UserSubmitted)));

        private sealed record Candidate(long Id, string Name, long CurationStatusId, string? Description, int? CheckVersion, bool Unsure = false, int Copied = 0);

        private sealed record Paraphrase(Candidate Recipe, ProseCheck.Finding Finding)
        {
            public string Verdict => Finding.Reasons.Contains(CheckErrorReason) ? CheckErrorReason
                : Finding.Sentences.Mostly ? Mostly : Finding.Sentences.Copied > 0 ? Some : None;

            public int Figure => Finding.Sentences.Kept;

            public bool Approved => Recipe.CurationStatusId == (long)CurationStatusEnum.Curated;
        }

        public async Task<ProseRecheckResult> RecheckAsync(bool apply = false, int maxRecipes = WithholdBatch, int sampleSize = 50, Func<CancellationToken, Task<bool>>? hold = null, CancellationToken cancellationToken = default)
        {
            var stock = await _stock.LoadAsync(cancellationToken);
            var result = new ProseRecheckResult { Applied = apply, CheckVersion = ProseCheck.Version, StockVersion = stock.Version };
            if (apply && stock.Version == 0)
            {
                result.Applied = false;
                result.Refused = "The stock-phrase index has not been built, and every run records the index it ran with; run POST api/ProseRewrite/stock/rebuild?apply=true first.";
            }

            result.ExemptClearLicence = await _db.Recipes.AsNoTracking().CountAsync(r => r.ScrapedAtUtc != null && !r.ContainsSourceProse
                && (r.LicenseStatus == RecipeLicenseStatus.PublicDomain || r.LicenseStatus == RecipeLicenseStatus.CreativeCommons || r.LicenseStatus == RecipeLicenseStatus.UserSubmitted), cancellationToken);
            var shown = await NotClear(_db.Recipes.AsNoTracking())
                .Where(r => !r.ContainsSourceProse && !r.ProseWithheld)
                .OrderBy(r => r.Id)
                .Select(r => new Candidate(r.Id, r.Name, r.CurationStatusId, r.Description, r.ProseCheckVersion, r.ProseUnsure, r.ProseCopiedSentences))
                .ToListAsync(cancellationToken);
            var held = await NotClear(_db.Recipes.AsNoTracking())
                .Where(r => !r.ContainsSourceProse && r.ProseWithheld)
                .OrderBy(r => r.Id)
                .Select(r => new Candidate(r.Id, r.Name, r.CurationStatusId, r.Description, r.ProseCheckVersion, r.ProseUnsure, r.ProseCopiedSentences))
                .ToListAsync(cancellationToken);
            result.Examined = shown.Count;

            var paraphrases = new List<Paraphrase>();
            var figures = new List<Paraphrase>();
            var cleared = new Dictionary<long, string>();
            foreach (var (pool, isShown) in new[] { (shown, true), (held, false) })
            {
                foreach (var chunk in pool.Chunk(RecipesPerRead))
                {
                    var (steps, sources, ended, laneWritten) = await ReadAsync(chunk, cancellationToken);
                    foreach (var candidate in chunk)
                    {
                        var own = steps[candidate.Id].OrderBy(s => s.StepNumber).ToList();
                        if (ended.Contains(candidate.Id) || EditedByPerson(own))
                        {
                            if (!isShown) continue;
                            result.SkippedEditedByPerson++;
                            if (result.SkippedEditedRecipeIds.Count < MaxListedRecipeIds) result.SkippedEditedRecipeIds.Add(candidate.Id);
                            if (candidate.Unsure) cleared[candidate.Id] = ended.Contains(candidate.Id) ? NotCheckedWithholdEnded : NotCheckedEdited;
                            continue;
                        }

                        if (laneWritten.Contains(candidate.Id))
                        {
                            if (isShown) result.ExemptLaneWritten++;
                            continue;
                        }

                        var raw = sources.GetValueOrDefault(candidate.Id);
                        if (RewriteFidelity.Source(raw) is not { } source)
                        {
                            if (isShown) result.SkippedNoSource++;
                            continue;
                        }

                        ProseCheck.Finding finding;
                        try
                        {
                            BeforeCheck?.Invoke(candidate.Id);
                            finding = ProseCheck.Check(
                                source,
                                new RewriteFidelity.SourceText(candidate.Description ?? string.Empty, own.Select(s => s.Description).ToList(), candidate.Name ?? string.Empty),
                                ProseCheck.SourceLines(raw),
                                stock);
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            var error = $"{ex.GetType().Name}: {ex.Message}";
                            result.Errored++;
                            if (result.Errors.Count < MaxListedRecipeIds)
                                result.Errors.Add(new ProseRecheckError { RecipeId = candidate.Id, Error = error });
                            finding = new ProseCheck.Finding(new[] { new ProseCheck.Fault(CheckErrorReason, error) }, 0m, 0, 0, 0m, stock.Version);
                        }

                        var paraphrase = new Paraphrase(candidate, finding);
                        result.ByCopy[paraphrase.Verdict] = result.ByCopy.GetValueOrDefault(paraphrase.Verdict) + 1;
                        if (paraphrase.Figure != candidate.Copied) figures.Add(paraphrase);
                        if (!isShown)
                        {
                            result.ExaminedWithheld++;
                            continue;
                        }

                        paraphrases.Add(paraphrase);
                        result.EarlierParaphrases++;
                        if (paraphrase.Approved) result.EarlierParaphrasesApproved++;
                    }
                }
            }

            var ordered = paraphrases
                .OrderBy(p => p.Approved ? 0 : 1)
                .ThenByDescending(p => p.Figure)
                .ThenBy(p => p.Recipe.Id)
                .ToList();
            result.Samples = ordered.Take(Math.Clamp(sampleSize, 0, 5000)).Select(Sample).ToList();
            result.QueueFigures = figures.Count;

            if (result.Applied)
            {
                var personId = _currentUser.PersonIdOrSystem;
                var applied = new List<Paraphrase>();
                foreach (var chunk in ordered.Take(Math.Clamp(maxRecipes, 1, 100000)).Chunk(WithholdBatch))
                {
                    var ids = chunk.Select(f => f.Recipe.Id).ToList();
                    await using var tx = _db.Database.IsRelational() ? await _db.Database.BeginTransactionAsync(cancellationToken) : null;
                    var recipes = await _db.Recipes.Where(r => ids.Contains(r.Id)).ToDictionaryAsync(r => r.Id, cancellationToken);
                    var now = DateTime.UtcNow;
                    var batch = new List<Paraphrase>();
                    foreach (var paraphrase in chunk)
                    {
                        if (!recipes.TryGetValue(paraphrase.Recipe.Id, out var recipe) || recipe.ContainsSourceProse || recipe.ProseWithheld) continue;

                        _db.AuditLogEntries.Add(new AuditLogEntryEntity
                        {
                            EntityType = RecipeEntityType,
                            EntityId = recipe.Id,
                            ChangeType = WithholdChangeType,
                            PropertyName = nameof(RecipeEntity.ProseWithheld),
                            OldValue = recipe.ProseCheckVersion?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                            NewValue = Clamp($"check {ProseCheck.Version} stock {stock.Version}: earlier paraphrase, withheld until reworded as a whole: {paraphrase.Verdict}: {paraphrase.Finding} | measures {paraphrase.Finding.Measures}"),
                            Timestamp = now,
                            ChangedByPersonId = personId,
                        });
                        if (recipe.ProseUnsure)
                        {
                            _db.AuditLogEntries.Add(UnsureEntry(recipe.Id, false, $"withheld as an earlier paraphrase: {paraphrase.Finding}", stock.Version, now, personId));
                            recipe.ProseUnsure = false;
                            result.ClearedUnsure++;
                        }

                        SetFigure(recipe, paraphrase, stock.Version, now, personId);
                        recipe.ProseWithheld = true;
                        recipe.ProseCheckVersion = ProseCheck.Version;
                        recipe.LastModifiedDate = now;
                        recipe.LastModifiedByPersonId = personId;
                        batch.Add(paraphrase);
                    }

                    if (hold != null && await hold(cancellationToken))
                    {
                        _db.ChangeTracker.Clear();
                        result.Held = true;
                        break;
                    }

                    await _db.SaveChangesAsync(cancellationToken);
                    if (tx != null) await tx.CommitAsync(cancellationToken);
                    _db.ChangeTracker.Clear();
                    result.Withheld += batch.Count;
                    applied.AddRange(batch);
                }

                if (!result.Held)
                {
                    await ClearUnsureAsync(cleared, stock.Version, personId, result, cancellationToken);
                    if (result.Withheld == ordered.Count)
                    {
                        var shownIds = paraphrases.Select(p => p.Recipe.Id).ToHashSet();
                        await SetFiguresAsync(figures.Where(f => !shownIds.Contains(f.Recipe.Id)).ToList(), stock.Version, personId, cancellationToken);
                    }
                }

                result.Samples = applied.Take(Math.Clamp(sampleSize, 0, 5000)).Select(Sample).ToList();
                result.WithheldByStatus = applied
                    .GroupBy(f => StatusName(f.Recipe.CurationStatusId))
                    .OrderBy(g => g.Key, StringComparer.Ordinal)
                    .ToDictionary(g => g.Key, g => g.Count());
            }

            result.Remaining = result.EarlierParaphrases - result.Withheld;
            if (result.Applied && !result.Held && result.Remaining == 0)
            {
                _db.AuditLogEntries.Add(new AuditLogEntryEntity
                {
                    EntityType = RunEntityType,
                    EntityId = ProseCheck.Version,
                    ChangeType = RunChangeType,
                    PropertyName = nameof(ProseRecheckResult.StockVersion),
                    NewValue = Clamp($"check {ProseCheck.Version} stock {stock.Version}: examined {result.Examined}, earlier paraphrases withheld {result.Withheld} ({result.EarlierParaphrasesApproved} approved), already withheld {result.ExaminedWithheld}, copied {string.Join(", ", result.ByCopy.OrderBy(v => v.Key, StringComparer.Ordinal).Select(v => $"{v.Key} {v.Value}"))}, written by the lane {result.ExemptLaneWritten}, skipped edited {result.SkippedEditedByPerson}, no source {result.SkippedNoSource}, errored {result.Errored}"),
                    Timestamp = DateTime.UtcNow,
                    ChangedByPersonId = _currentUser.PersonIdOrSystem,
                });
                await _db.SaveChangesAsync(cancellationToken);
                result.RunRecorded = true;
            }

            return result;
        }

        private void SetFigure(RecipeEntity recipe, Paraphrase paraphrase, int stockVersion, DateTime now, long personId)
        {
            if (recipe.ProseCopiedSentences == paraphrase.Figure) return;
            _db.AuditLogEntries.Add(RepairEntry(recipe.Id, recipe.ProseCopiedSentences, paraphrase.Figure, $"queue figure: {paraphrase.Verdict}: {paraphrase.Finding.Sentences}", stockVersion, now, personId));
            recipe.ProseCopiedSentences = paraphrase.Figure;
        }

        private async Task SetFiguresAsync(List<Paraphrase> figures, int stockVersion, long personId, CancellationToken cancellationToken)
        {
            foreach (var chunk in figures.Chunk(RecipesPerTransaction))
            {
                var byId = chunk.ToDictionary(p => p.Recipe.Id);
                var ids = byId.Keys.ToList();
                var recipes = await _db.Recipes.Where(r => ids.Contains(r.Id) && r.ProseWithheld && !r.ContainsSourceProse).ToListAsync(cancellationToken);
                var now = DateTime.UtcNow;
                foreach (var recipe in recipes) SetFigure(recipe, byId[recipe.Id], stockVersion, now, personId);
                await _db.SaveChangesAsync(cancellationToken);
                _db.ChangeTracker.Clear();
            }
        }

        private sealed record Step(long RecipeId, int StepNumber, string Description, long? LastModifiedByPersonId);

        private async Task<(ILookup<long, Step> Steps, Dictionary<long, string?> Sources, HashSet<long> Ended, HashSet<long> LaneWritten)> ReadAsync(IEnumerable<Candidate> chunk, CancellationToken cancellationToken)
        {
            var ids = chunk.Select(c => c.Id).ToList();
            var steps = (await _db.RecipeSteps.AsNoTracking()
                    .Where(s => ids.Contains(s.RecipeId))
                    .Select(s => new Step(s.RecipeId, s.StepNumber, s.Description, s.LastModifiedByPersonId))
                    .ToListAsync(cancellationToken))
                .ToLookup(s => s.RecipeId);
            var sources = (await _db.ScrapedDocuments.AsNoTracking()
                    .Where(d => ids.Contains(d.RecipeId) && d.Format == ScrapedDocumentFormat.JsonLd)
                    .OrderBy(d => d.Id)
                    .Select(d => new { d.RecipeId, d.RawJsonLd })
                    .ToListAsync(cancellationToken))
                .GroupBy(d => d.RecipeId)
                .ToDictionary(g => g.Key, g => (string?)g.Last().RawJsonLd);
            var ended = (await _db.AuditLogEntries.AsNoTracking()
                    .Where(a => a.EntityType == RecipeEntityType && a.ChangeType == WithholdEndChangeType && ids.Contains(a.EntityId))
                    .Select(a => a.EntityId)
                    .ToListAsync(cancellationToken))
                .ToHashSet();
            var states = await _db.AuditLogEntries.AsNoTracking()
                .Where(a => a.EntityType == RecipeEntityType && a.ChangeType == RewriteChangeType && a.PropertyName == ProseLaneJournal.StateProperty && ids.Contains(a.EntityId))
                .Select(a => new { a.Id, a.EntityId, a.NewValue })
                .ToListAsync(cancellationToken);
            var undone = (await _db.AuditLogEntries.AsNoTracking()
                    .Where(a => a.EntityType == RecipeEntityType && a.ChangeType == ProseLaneJournal.UndoChangeType && ids.Contains(a.EntityId))
                    .Select(a => a.OldValue)
                    .ToListAsync(cancellationToken))
                .Select(v => long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : 0)
                .ToHashSet();
            var laneWritten = states
                .Where(a => !undone.Contains(a.Id))
                .GroupBy(a => a.EntityId)
                .Where(g => ProseLanePrompts.IsWholeMethodTag(g.OrderByDescending(a => a.Id).First().NewValue))
                .Select(g => g.Key)
                .ToHashSet();
            return (steps, sources, ended, laneWritten);
        }

        private static bool EditedByPerson(IEnumerable<Step> steps) =>
            steps.Any(s => s.LastModifiedByPersonId is { } by && by != SystemConstants.SystemPersonId);

        public async Task<ProseRecheckRun?> LastRunAsync(CancellationToken cancellationToken = default)
        {
            var last = await _db.AuditLogEntries.AsNoTracking()
                .Where(a => a.EntityType == RunEntityType && a.ChangeType == RunChangeType)
                .OrderByDescending(a => a.Id)
                .Select(a => new { a.Timestamp, a.NewValue })
                .FirstOrDefaultAsync(cancellationToken);
            if (last == null) return null;
            var m = RunVersions.Match(last.NewValue ?? string.Empty);
            return m.Success
                ? new ProseRecheckRun(last.Timestamp, int.Parse(m.Groups["check"].Value, CultureInfo.InvariantCulture), int.Parse(m.Groups["stock"].Value, CultureInfo.InvariantCulture))
                : new ProseRecheckRun(last.Timestamp, 0, 0);
        }

        public async Task<string?> DueAsync(TimeSpan interval, DateTime now, CancellationToken cancellationToken = default)
        {
            var stockVersion = await _db.ProseStockPhrases.AsNoTracking().MaxAsync(p => (int?)p.Version, cancellationToken) ?? 0;
            return Due(await LastRunAsync(cancellationToken), ProseCheck.Version, stockVersion, interval, now);
        }

        /// <summary>Why a full run is due, or null when none is: never while no stock-phrase index exists; then when none has run, when the check or the index has a new version, or when <paramref name="interval"/> has passed since the last.</summary>
        public static string? Due(ProseRecheckRun? last, int checkVersion, int stockVersion, TimeSpan interval, DateTime now)
        {
            if (stockVersion == 0) return null;
            if (last == null) return "no full run recorded";
            if (last.CheckVersion != checkVersion) return $"prose check version {last.CheckVersion} -> {checkVersion}";
            if (last.StockVersion != stockVersion) return $"stock index version {last.StockVersion} -> {stockVersion}";
            return now - last.At >= interval ? $"last full run {last.At:yyyy-MM-dd HH:mm} UTC" : null;
        }

        private static ProseRecheckSample Sample(Paraphrase p) => new()
        {
            RecipeId = p.Recipe.Id,
            Name = p.Recipe.Name,
            Status = StatusName(p.Recipe.CurationStatusId),
            Copied = p.Verdict,
            Reasons = p.Finding.Reasons.ToList(),
            Detail = p.Finding.ToString(),
        };

        public async Task<ProseRecheckUndoResult> UndoAsync(DateTime sinceUtc, bool apply = false)
        {
            var result = new ProseRecheckUndoResult { Applied = apply };
            await UndoRequarantineAsync(sinceUtc, apply, result);
            await UndoWithholdAsync(sinceUtc, apply, result);
            await UndoUnsureAsync(sinceUtc, apply, result);
            await UndoRepairAsync(sinceUtc, apply, result);
            return result;
        }

        private AuditLogEntryEntity UnsureEntry(long recipeId, bool mark, string why, int stockVersion, DateTime now, long personId) => new()
        {
            EntityType = RecipeEntityType,
            EntityId = recipeId,
            ChangeType = UnsureChangeType,
            PropertyName = nameof(RecipeEntity.ProseUnsure),
            OldValue = mark ? "false" : "true",
            NewValue = Clamp($"{(mark ? "true" : "false")} | check {ProseCheck.Version} stock {stockVersion}: {why}"),
            Timestamp = now,
            ChangedByPersonId = personId,
        };

        private AuditLogEntryEntity RepairEntry(long recipeId, int before, int after, string why, int stockVersion, DateTime now, long personId) => new()
        {
            EntityType = RecipeEntityType,
            EntityId = recipeId,
            ChangeType = RepairChangeType,
            PropertyName = nameof(RecipeEntity.ProseCopiedSentences),
            OldValue = before.ToString(CultureInfo.InvariantCulture),
            NewValue = Clamp($"{after.ToString(CultureInfo.InvariantCulture)} | check {ProseCheck.Version} stock {stockVersion}: {why}"),
            Timestamp = now,
            ChangedByPersonId = personId,
        };

        private async Task UndoRepairAsync(DateTime sinceUtc, bool apply, ProseRecheckUndoResult result)
        {
            var entries = await _db.AuditLogEntries.AsNoTracking()
                .Where(a => a.EntityType == RecipeEntityType && a.ChangeType == RepairChangeType && a.Timestamp >= sinceUtc)
                .OrderBy(a => a.Id)
                .ToListAsync();
            var earliest = entries.GroupBy(e => e.EntityId)
                .ToDictionary(g => g.Key, g => int.TryParse(g.First().OldValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0);
            foreach (var chunk in earliest.Keys.Chunk(RecipesPerTransaction))
            {
                var recipes = await _db.Recipes.Where(r => chunk.Contains(r.Id)).ToListAsync();
                var now = DateTime.UtcNow;
                foreach (var recipe in recipes.Where(r => r.ProseCopiedSentences != earliest[r.Id]))
                {
                    result.RestoredRepair++;
                    if (!apply) continue;
                    _db.AuditLogEntries.Add(new AuditLogEntryEntity
                    {
                        EntityType = RecipeEntityType,
                        EntityId = recipe.Id,
                        ChangeType = RepairChangeType,
                        PropertyName = nameof(RecipeEntity.ProseCopiedSentences),
                        OldValue = recipe.ProseCopiedSentences.ToString(CultureInfo.InvariantCulture),
                        NewValue = earliest[recipe.Id].ToString(CultureInfo.InvariantCulture) + $" | undo since {sinceUtc:O}",
                        Timestamp = now,
                        ChangedByPersonId = _currentUser.PersonIdOrSystem,
                    });
                    recipe.ProseCopiedSentences = earliest[recipe.Id];
                }

                if (!apply) continue;
                await _db.SaveChangesAsync();
                _db.ChangeTracker.Clear();
            }
        }

        private async Task ClearUnsureAsync(Dictionary<long, string> cleared, int stockVersion, long personId, ProseRecheckResult result, CancellationToken cancellationToken)
        {
            foreach (var chunk in cleared.Keys.Chunk(RecipesPerTransaction))
            {
                var recipes = await _db.Recipes.Where(r => chunk.Contains(r.Id) && r.ProseUnsure).ToListAsync(cancellationToken);
                var now = DateTime.UtcNow;
                foreach (var recipe in recipes)
                {
                    _db.AuditLogEntries.Add(UnsureEntry(recipe.Id, false, cleared[recipe.Id], stockVersion, now, personId));
                    recipe.ProseUnsure = false;
                    result.ClearedUnsure++;
                }

                await _db.SaveChangesAsync(cancellationToken);
                _db.ChangeTracker.Clear();
            }
        }

        private async Task UndoUnsureAsync(DateTime sinceUtc, bool apply, ProseRecheckUndoResult result)
        {
            var entries = await _db.AuditLogEntries.AsNoTracking()
                .Where(a => a.EntityType == RecipeEntityType && a.ChangeType == UnsureChangeType && a.Timestamp >= sinceUtc)
                .OrderBy(a => a.Id)
                .ToListAsync();
            var earliest = entries.GroupBy(e => e.EntityId).ToDictionary(g => g.Key, g => g.First().OldValue == "true");
            foreach (var chunk in earliest.Keys.Chunk(RecipesPerTransaction))
            {
                var recipes = await _db.Recipes.Where(r => chunk.Contains(r.Id)).ToListAsync();
                var now = DateTime.UtcNow;
                foreach (var recipe in recipes.Where(r => r.ProseUnsure != earliest[r.Id]))
                {
                    result.RestoredUnsure++;
                    if (!apply) continue;
                    _db.AuditLogEntries.Add(new AuditLogEntryEntity
                    {
                        EntityType = RecipeEntityType,
                        EntityId = recipe.Id,
                        ChangeType = UnsureChangeType,
                        PropertyName = nameof(RecipeEntity.ProseUnsure),
                        OldValue = recipe.ProseUnsure ? "true" : "false",
                        NewValue = (earliest[recipe.Id] ? "true" : "false") + $" | undo since {sinceUtc:O}",
                        Timestamp = now,
                        ChangedByPersonId = _currentUser.PersonIdOrSystem,
                    });
                    recipe.ProseUnsure = earliest[recipe.Id];
                }

                if (!apply) continue;
                await _db.SaveChangesAsync();
                _db.ChangeTracker.Clear();
            }
        }

        private async Task UndoWithholdAsync(DateTime sinceUtc, bool apply, ProseRecheckUndoResult result)
        {
            var undone = (await _db.AuditLogEntries.AsNoTracking()
                    .Where(a => a.EntityType == RecipeEntityType && a.ChangeType == WithholdUndoChangeType && a.Timestamp >= sinceUtc)
                    .Select(a => a.OldValue)
                    .ToListAsync())
                .Select(v => long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : 0)
                .ToHashSet();
            var entries = (await _db.AuditLogEntries.AsNoTracking()
                    .Where(a => a.EntityType == RecipeEntityType && a.ChangeType == WithholdChangeType && a.Timestamp >= sinceUtc)
                    .OrderByDescending(a => a.Id)
                    .ToListAsync())
                .Where(a => !undone.Contains(a.Id))
                .ToList();
            var personId = _currentUser.PersonIdOrSystem;

            foreach (var chunk in entries.Chunk(RecipesPerTransaction))
            {
                await using var tx = apply && _db.Database.IsRelational() ? await _db.Database.BeginTransactionAsync() : null;
                var ids = chunk.Select(e => e.EntityId).Distinct().ToList();
                var recipes = await _db.Recipes.Where(r => ids.Contains(r.Id)).ToDictionaryAsync(r => r.Id);
                var rewrites = await _db.AuditLogEntries.AsNoTracking()
                    .Where(a => a.EntityType == RecipeEntityType && ids.Contains(a.EntityId) && a.ChangeType == RewriteChangeType)
                    .Select(a => new { a.EntityId, a.Id, a.Timestamp, a.PropertyName })
                    .ToListAsync();
                var laneUndone = (await _db.AuditLogEntries.AsNoTracking()
                        .Where(a => a.EntityType == RecipeEntityType && ids.Contains(a.EntityId) && a.ChangeType == ProseLaneJournal.UndoChangeType)
                        .Select(a => a.OldValue)
                        .ToListAsync())
                    .Select(v => long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : 0)
                    .ToHashSet();
                var undoneGroups = rewrites
                    .Where(a => a.PropertyName == ProseLaneJournal.StateProperty && laneUndone.Contains(a.Id))
                    .Select(a => (a.EntityId, a.Timestamp))
                    .ToHashSet();
                var rewrittenAfter = rewrites
                    .Where(a => !undoneGroups.Contains((a.EntityId, a.Timestamp)))
                    .GroupBy(a => a.EntityId)
                    .ToDictionary(g => g.Key, g => g.Max(a => a.Id));
                var now = DateTime.UtcNow;
                foreach (var entry in chunk)
                {
                    if (!recipes.TryGetValue(entry.EntityId, out var recipe))
                    {
                        result.Skipped++;
                        continue;
                    }

                    if (!recipe.ProseWithheld || rewrittenAfter.GetValueOrDefault(entry.EntityId) > entry.Id)
                    {
                        result.Skipped++;
                        result.SkippedRewritten++;
                        continue;
                    }

                    result.Restored++;
                    result.RestoredWithheld++;
                    if (!apply) continue;
                    _db.AuditLogEntries.Add(new AuditLogEntryEntity
                    {
                        EntityType = RecipeEntityType,
                        EntityId = recipe.Id,
                        ChangeType = WithholdUndoChangeType,
                        PropertyName = nameof(RecipeEntity.ProseWithheld),
                        OldValue = entry.Id.ToString(CultureInfo.InvariantCulture),
                        NewValue = "false",
                        Timestamp = now,
                        ChangedByPersonId = personId,
                    });
                    recipe.ProseWithheld = false;
                    recipe.ProseCheckVersion = int.TryParse(entry.OldValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var version) ? version : null;
                    recipe.LastModifiedDate = now;
                    recipe.LastModifiedByPersonId = personId;
                }

                if (!apply) continue;
                await _db.SaveChangesAsync();
                if (tx != null) await tx.CommitAsync();
                _db.ChangeTracker.Clear();
            }

            result.Remaining = apply ? 0 : result.Restored;
        }

        private async Task UndoRequarantineAsync(DateTime sinceUtc, bool apply, ProseRecheckUndoResult result)
        {
            var entries = await _db.AuditLogEntries
                .Where(a => a.EntityType == RecipeEntityType && a.ChangeType == RecheckChangeType && a.Timestamp >= sinceUtc)
                .OrderByDescending(a => a.Id)
                .ToListAsync();
            var personId = _currentUser.PersonIdOrSystem;

            foreach (var chunk in entries.Chunk(RecipesPerTransaction))
            {
                await using var tx = apply && _db.Database.IsRelational() ? await _db.Database.BeginTransactionAsync() : null;
                var ids = chunk.Select(e => e.EntityId).Distinct().ToList();
                var recipes = await _db.Recipes.Where(r => ids.Contains(r.Id)).ToDictionaryAsync(r => r.Id);
                var now = DateTime.UtcNow;
                foreach (var entry in chunk)
                {
                    if (!recipes.TryGetValue(entry.EntityId, out var recipe))
                    {
                        result.Skipped++;
                        continue;
                    }

                    if (!recipe.ContainsSourceProse)
                    {
                        result.Skipped++;
                        result.SkippedRewritten++;
                        continue;
                    }

                    result.Restored++;
                    if (recipe.ProseRewriteAttemptedAt != null) result.RestoredAfterFailedAttempt++;
                    if (!apply) continue;
                    recipe.ContainsSourceProse = false;
                    recipe.ProseRewriteAttemptedAt = DateTime.TryParse(entry.OldValue, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at)
                        ? at
                        : null;
                    recipe.LastModifiedDate = now;
                    recipe.LastModifiedByPersonId = personId;
                    _db.AuditLogEntries.Remove(entry);
                }

                if (!apply) continue;
                await _db.SaveChangesAsync();
                if (tx != null) await tx.CommitAsync();
            }
        }

        public static string Clamp(string value) => value.Length <= JournalLength ? value : value[..JournalLength];

        private static string StatusName(long statusId) =>
            Enum.IsDefined(typeof(CurationStatusEnum), statusId)
                ? ((CurationStatusEnum)statusId).ToString()
                : statusId.ToString(CultureInfo.InvariantCulture);
    }
}

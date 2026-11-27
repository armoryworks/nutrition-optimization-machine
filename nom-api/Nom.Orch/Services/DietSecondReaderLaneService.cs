using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Nom.Data;
using Nom.Data.Curation;
using Nom.Data.Plan;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;
using Nom.Orch.Services.Support;

namespace Nom.Orch.Services
{
    /// <summary>One batch of the second reader's shadow lane: the recipes it read, the readings it stored, and how many of those the host gave no readable reply for.</summary>
    public sealed record DietSecondBatch(int Recipes, int Readings, int Unreadable);

    /// <summary>One model's part of the lane's progress: readings current for the recipe's text and prompt, readings stale (older text or prompt), unreadable replies, recipes left, and the hours left at its seconds per recipe.</summary>
    public sealed record DietSecondModelProgress(string Model, int Current, int Stale, int Unreadable, int Left, double SecondsPerRecipe, bool SecondsMeasured, double HoursLeft);

    /// <summary>
    /// The lane's progress over its scope (<c>GET /api/DietAdmin/second-reader</c>): the approved recipes (the public pool) and
    /// the recipes waiting for approval that only the second reading keeps waiting, how many the pair has read under the current prompt and text, per model, and the
    /// hours of model time left one job at a time.
    /// </summary>
    public sealed record DietSecondProgress(bool GateOn, string Prompt, IReadOnlyList<string> Models, int Approved, int Waiting, int InScope, int ReadByPair, int ApprovedReadByPair,
        IReadOnlyList<DietSecondModelProgress> PerModel, double HoursLeft, DateTime? LastReadAt);

    /// <summary>
    /// The second diet reader as a shadow lane (plan item ck21, lane diet-second-reader): it reads, with both models of
    /// <see cref="DietSecondGate.Pair"/>, every approved recipe of the public pool and then every recipe waiting for
    /// approval that only the second reading keeps waiting (<see cref="ApprovableAsync"/>), and stores each model's reading per recipe (<see cref="RecipeDietReadingEntity"/>) with the model, the
    /// reader's prompt signature, the signature of the text it was shown and the store version, journaling each in
    /// curation.LaneDecision (mode shadow; it writes nothing a household sees). It never reads a recipe again while its text,
    /// the prompt and the model are unchanged, so a stopped lane resumes where it was; a reply the host did not give in a
    /// readable form is kept as unreadable (it holds the recipe) and read again after DietSecondReader:RetryUnreadableHours
    /// (default 24). A host that cannot be reached or fails (no reply, or a 5xx status) is not a reply: the batch stops with
    /// the fault and stores nothing for that recipe. The readings reach households only through the stored gate, and only while the platform setting
    /// <see cref="DietSecondGate.FeatureKey"/> is on. The host is DietSecondReader:OllamaUrl, else Ai:BatchOllamaUrl, else
    /// Ai:OllamaUrl.
    /// </summary>
    public class DietSecondReaderLaneService
    {
        /// <summary>The seconds per recipe measured in ck20 on the shared host, used for the hours left until the lane has measured its own.</summary>
        public static readonly IReadOnlyDictionary<string, double> MeasuredSeconds = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["mistral-small:24b"] = 22.1,
            ["phi4:latest"] = 13.1,
        };

        private const int Chunk = 1000;

        private readonly ApplicationDbContext _db;
        private readonly IReadOnlyList<OllamaDietSecondReader> _readers;
        private readonly TimeSpan _retryUnreadable;
        private readonly AutoApprovalOptions _options;
        private readonly IRecipeVettingService _vetting;

        public DietSecondReaderLaneService(ApplicationDbContext db, HttpClient httpClient, IConfiguration configuration, IRecipeVettingService? vetting = null)
        {
            _db = db;
            _options = RecipeAutoCurationService.OptionsFrom(configuration);
            _vetting = vetting ?? new RecipeVettingService();
            var host = Host(configuration);
            _readers = DietSecondGate.Pair.Select(m => new OllamaDietSecondReader(httpClient, host, m)).ToList();
            _retryUnreadable = TimeSpan.FromHours(Math.Max(0, configuration.GetValue("DietSecondReader:RetryUnreadableHours", 24.0)));
        }

        public static string Host(IConfiguration configuration) =>
            new[] { configuration["DietSecondReader:OllamaUrl"], configuration["Ai:BatchOllamaUrl"], configuration["Ai:OllamaUrl"] }.FirstOrDefault(h => !string.IsNullOrWhiteSpace(h)) ?? string.Empty;

        public bool IsConfigured => _readers.All(r => r.IsConfigured);

        /// <summary>The public pool: the recipes households see (<c>RecipeVisibilityExtensions.VisibleTo</c>'s public part).</summary>
        public static IQueryable<RecipeEntity> Approved(ApplicationDbContext db) =>
            db.Recipes.IgnoreQueryFilters().AsNoTracking().Where(r => !r.IsDeleted && r.Visibility == RecipeVisibilityEnum.Public
                && r.CurationStatusId == (long)CurationStatusEnum.Curated && !r.ContainsSourceProse && r.SourceWithheldById == null);

        /// <summary>The recipes waiting for approval: those the auto-approval sweep examines (<see cref="RecipeAutoCurationService.Candidates"/>).</summary>
        public static IQueryable<RecipeEntity> Waiting(ApplicationDbContext db, bool requireImage = true) =>
            RecipeAutoCurationService.Candidates(db, requireImage).AsNoTracking();

        /// <summary>
        /// The waiting recipes the lane reads, by id: those the sweep examines under the configured Curation:AutoApprove
        /// options (<see cref="Waiting"/>) that pass its policy and vetting and wait for nothing but the second reading
        /// (<see cref="DietApprovalHolds"/>), so every reading of a waiting recipe is one the sweep needs before it can approve it.
        /// </summary>
        public async Task<List<long>> ApprovableAsync(CancellationToken cancellationToken = default)
        {
            var result = new List<long>();
            var holds = _options with { RequireSecondReading = false };
            long after = 0;
            while (true)
            {
                var query = Waiting(_db, _options.RequireOwnImageForUnknownLicense).Where(r => r.Id > after).OrderBy(r => r.Id).Take(Chunk)
                    .Include(r => r.RecipeIngredients!).ThenInclude(ri => ri.Ingredient)
                    .Include(r => r.RecipeSteps);
                var batch = _db.Database.IsRelational()
                    ? await query.AsSplitQuery().ToListAsync(cancellationToken)
                    : await query.ToListAsync(cancellationToken);
                if (batch.Count == 0) return result;
                after = batch[^1].Id;
                var passed = new List<RecipeEntity>();
                foreach (var recipe in batch)
                    if (await RecipeAutoCurationService.RefusalAsync(recipe, _options, _vetting) == null) passed.Add(recipe);
                var waiting = await DietApprovalHolds.ReasonsAsync(_db, passed, holds, cancellationToken);
                result.AddRange(passed.Where(r => !waiting.ContainsKey(r.Id)).Select(r => r.Id));
            }
        }

        private Task<List<long>> ApprovedIdsAsync(CancellationToken cancellationToken) =>
            Approved(_db).OrderBy(r => r.Id).Select(r => r.Id).ToListAsync(cancellationToken);

        /// <summary>The lane's scope in the order it reads: approved recipes first, then those waiting (<see cref="ApprovableAsync"/>), each by id.</summary>
        public async Task<(List<long> Approved, List<long> Waiting)> ScopeAsync(CancellationToken cancellationToken = default) =>
            (await ApprovedIdsAsync(cancellationToken), await ApprovableAsync(cancellationToken));

        /// <summary>The text the reader is shown for each recipe, as the gate signs it (<see cref="DietSecondGate.TextSignature"/>).</summary>
        public async Task<Dictionary<long, (DietSecondRecipe Recipe, string Signature)>> TextsAsync(IReadOnlyCollection<long> recipeIds, CancellationToken cancellationToken = default)
        {
            var titles = await _db.Recipes.IgnoreQueryFilters().AsNoTracking().Where(r => recipeIds.Contains(r.Id)).Select(r => new { r.Id, r.Name }).ToListAsync(cancellationToken);
            var lines = (await (from ri in _db.RecipeIngredients.IgnoreQueryFilters().AsNoTracking()
                                join i in _db.Ingredients.IgnoreQueryFilters().AsNoTracking() on ri.IngredientId equals i.Id into food
                                from i in food.DefaultIfEmpty()
                                where !ri.IsDeleted && recipeIds.Contains(ri.RecipeId)
                                select new { ri.RecipeId, ri.SortOrder, ri.IngredientId, ri.RawLine, Food = i == null ? null : i.Name })
                    .ToListAsync(cancellationToken))
                .GroupBy(l => l.RecipeId)
                .ToDictionary(g => g.Key, g => g.OrderBy(l => l.SortOrder).ThenBy(l => l.IngredientId).Select(l => DietSecondGate.LineText(l.RawLine, l.Food)).ToList());
            var steps = (await _db.RecipeSteps.IgnoreQueryFilters().AsNoTracking()
                    .Where(s => !s.IsDeleted && recipeIds.Contains(s.RecipeId))
                    .Select(s => new { s.RecipeId, s.StepNumber, s.Description })
                    .ToListAsync(cancellationToken))
                .GroupBy(s => s.RecipeId)
                .ToDictionary(g => g.Key, g => g.OrderBy(s => s.StepNumber).Select(s => s.Description).ToList());
            var result = new Dictionary<long, (DietSecondRecipe, string)>();
            foreach (var t in titles)
            {
                var recipeLines = lines.GetValueOrDefault(t.Id) ?? new List<string>();
                var recipeSteps = steps.GetValueOrDefault(t.Id) ?? new List<string>();
                var recipe = new DietSecondRecipe(t.Name ?? string.Empty, recipeLines.Where(l => !string.IsNullOrWhiteSpace(l)).ToList(),
                    recipeSteps.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToList());
                result[t.Id] = (recipe, DietSecondGate.TextSignature(t.Name, recipeLines, recipeSteps));
            }
            return result;
        }

        private sealed record Row(string Model, string Prompt, string Text, bool Readable, DateTime ReadAt);

        private async Task<Dictionary<long, List<Row>>> RowsAsync(IReadOnlyCollection<long> recipeIds, CancellationToken cancellationToken) =>
            (await _db.RecipeDietReadings.AsNoTracking().Where(r => recipeIds.Contains(r.RecipeId))
                    .Select(r => new { r.RecipeId, r.Model, r.PromptSignature, r.TextSignature, r.Readable, r.ReadAt })
                    .ToListAsync(cancellationToken))
                .GroupBy(r => r.RecipeId)
                .ToDictionary(g => g.Key, g => g.Select(r => new Row(r.Model, r.PromptSignature, r.TextSignature, r.Readable, r.ReadAt)).ToList());

        /// <summary>Whether a model must read a recipe: it has no reading of the current prompt and text, or only an unreadable one older than the retry time.</summary>
        private bool Needs(IReadOnlyList<Row>? rows, string model, string text, DateTime now)
        {
            var row = rows?.FirstOrDefault(r => r.Model == model);
            if (row == null || row.Prompt != OllamaDietSecondReader.Signature || row.Text != text) return true;
            return !row.Readable && now - row.ReadAt >= _retryUnreadable;
        }

        /// <summary>
        /// Reads the next recipes of the scope that a model of the pair must read, at most <paramref name="maxRecipes"/>, and
        /// stores each reading as it comes. <paramref name="paused"/> is asked before each recipe, so a pause stops the batch
        /// at the next recipe.
        /// </summary>
        public async Task<DietSecondBatch> RunBatchAsync(int maxRecipes, Func<Task<bool>>? paused = null, CancellationToken cancellationToken = default)
        {
            if (!IsConfigured) throw new InvalidOperationException("No Ollama URL configured for the second diet reader (DietSecondReader:OllamaUrl, Ai:BatchOllamaUrl or Ai:OllamaUrl).");
            int recipes = 0, readings = 0, unreadable = 0;
            async Task<bool> ReadIdsAsync(List<long> ids)
            {
                foreach (var chunk in ids.Chunk(Chunk))
                {
                    var texts = await TextsAsync(chunk, cancellationToken);
                    var rows = await RowsAsync(chunk, cancellationToken);
                    var now = DateTime.UtcNow;
                    foreach (var id in chunk)
                    {
                        if (recipes >= maxRecipes) return false;
                        if (!texts.TryGetValue(id, out var text)) continue;
                        var due = _readers.Where(r => Needs(rows.GetValueOrDefault(id), r.ModelName, text.Signature, now)).ToList();
                        if (due.Count == 0) continue;
                        if (paused != null && await paused()) return false;
                        foreach (var reader in due)
                        {
                            var readable = await ReadAndStoreAsync(reader, id, text.Recipe, text.Signature, cancellationToken);
                            readings++;
                            if (!readable) unreadable++;
                        }
                        recipes++;
                    }
                }
                return true;
            }
            if (await ReadIdsAsync(await ApprovedIdsAsync(cancellationToken))) await ReadIdsAsync(await ApprovableAsync(cancellationToken));
            return new DietSecondBatch(recipes, readings, unreadable);
        }

        private static bool HostFault(HttpRequestException ex) => ex.StatusCode == null || (int)ex.StatusCode >= 500;

        private async Task<bool> ReadAndStoreAsync(OllamaDietSecondReader reader, long recipeId, DietSecondRecipe recipe, string textSignature, CancellationToken cancellationToken)
        {
            var clock = Stopwatch.StartNew();
            DietSecondRead? read;
            string? failure = null;
            try
            {
                read = await reader.ReadAsync(recipe, cancellationToken);
            }
            catch (Exception ex) when ((ex is HttpRequestException http ? !HostFault(http) : ex is TaskCanceledException or JsonException) && !cancellationToken.IsCancellationRequested)
            {
                read = null;
                failure = ex.GetType().Name;
            }
            clock.Stop();
            var now = DateTime.UtcNow;
            var letters = read == null ? string.Empty : DietSecondGate.Letters(read);
            var answer = read == null ? null : LaneJournal.Write(new
            {
                read.Unlisted,
                read.Complete,
                Answers = read.Answers.ToDictionary(a => a.Type, a => new[] { OllamaDietSecondReader.WordOf(a.Reading), a.Food }),
            });
            var row = await _db.RecipeDietReadings.FirstOrDefaultAsync(r => r.RecipeId == recipeId && r.Model == reader.ModelName, cancellationToken);
            if (row == null)
            {
                row = new RecipeDietReadingEntity { RecipeId = recipeId, Model = reader.ModelName };
                _db.RecipeDietReadings.Add(row);
            }
            row.PromptSignature = OllamaDietSecondReader.Signature;
            row.TextSignature = textSignature;
            row.StoreVersion = DietSecondGate.Version;
            row.Readable = read != null;
            row.Complete = read?.Complete ?? false;
            row.Verdicts = letters;
            row.Answer = answer;
            row.Seconds = Math.Round(clock.Elapsed.TotalSeconds, 2);
            row.ReadAt = now;
            _db.LaneDecisions.Add(new LaneDecisionEntity
            {
                Lane = AiLanes.DietSecondReader,
                Tier = reader.ModelName,
                Subject = "recipe:" + recipeId,
                Decision = read == null ? "unreadable" : letters,
                Mode = LaneMode.Shadow,
                Writes = false,
                Written = false,
                SignatureHash = OllamaDietSecondReader.Signature,
                SignalsJson = LaneJournal.Write(new { Text = textSignature, Store = DietSecondGate.Version, Seconds = row.Seconds, Failure = failure }),
                VerdictsJson = answer,
                CreatedDate = now,
                CreatedByPersonId = SystemConstants.SystemPersonId,
            });
            await _db.SaveChangesAsync(cancellationToken);
            _db.ChangeTracker.Clear();
            return read != null;
        }

        /// <summary>The lane's progress over its whole scope, and the hours of model time left at each model's mean seconds per recipe (its own readings, or ck20's measure until it has 20).</summary>
        public async Task<DietSecondProgress> ProgressAsync(CancellationToken cancellationToken = default)
        {
            var (approved, waiting) = await ScopeAsync(cancellationToken);
            var approvedSet = approved.ToHashSet();
            var perModel = DietSecondGate.Pair.ToDictionary(m => m, _ => (Current: 0, Stale: 0, Unreadable: 0, Left: 0));
            int byPair = 0, approvedByPair = 0;
            foreach (var chunk in approved.Concat(waiting).Chunk(Chunk))
            {
                var texts = await TextsAsync(chunk, cancellationToken);
                var rows = await RowsAsync(chunk, cancellationToken);
                foreach (var id in chunk)
                {
                    if (!texts.TryGetValue(id, out var text)) continue;
                    var all = true;
                    foreach (var model in DietSecondGate.Pair)
                    {
                        var row = rows.GetValueOrDefault(id)?.FirstOrDefault(r => r.Model == model);
                        var p = perModel[model];
                        if (row != null && row.Prompt == OllamaDietSecondReader.Signature && row.Text == text.Signature)
                        {
                            if (row.Readable) p.Current++;
                            else
                            {
                                p.Unreadable++;
                                p.Left++;
                                all = false;
                            }
                        }
                        else
                        {
                            if (row != null) p.Stale++;
                            p.Left++;
                            all = false;
                        }
                        perModel[model] = p;
                    }
                    if (!all) continue;
                    byPair++;
                    if (approvedSet.Contains(id)) approvedByPair++;
                }
            }
            var seconds = await _db.RecipeDietReadings.AsNoTracking().Where(r => r.Readable && DietSecondGate.Pair.Contains(r.Model))
                .GroupBy(r => r.Model).Select(g => new { Model = g.Key, Count = g.Count(), Mean = g.Average(r => r.Seconds) }).ToListAsync(cancellationToken);
            var models = new List<DietSecondModelProgress>();
            foreach (var model in DietSecondGate.Pair)
            {
                var own = seconds.FirstOrDefault(s => s.Model == model);
                var measured = own != null && own.Count >= 20;
                var perRecipe = measured ? own!.Mean : MeasuredSeconds.GetValueOrDefault(model, 20.0);
                var p = perModel[model];
                models.Add(new DietSecondModelProgress(model, p.Current, p.Stale, p.Unreadable, p.Left, Math.Round(perRecipe, 1), measured, Math.Round(p.Left * perRecipe / 3600.0, 1)));
            }
            var last = await _db.RecipeDietReadings.AsNoTracking().OrderByDescending(r => r.ReadAt).Select(r => (DateTime?)r.ReadAt).FirstOrDefaultAsync(cancellationToken);
            return new DietSecondProgress(await DietSecondGate.OnAsync(_db, cancellationToken), OllamaDietSecondReader.Signature, DietSecondGate.Pair, approved.Count, waiting.Count,
                approved.Count + waiting.Count, byPair, approvedByPair, models, Math.Round(models.Sum(m => m.HoursLeft), 1), last);
        }
    }
}

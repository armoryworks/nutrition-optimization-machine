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
using Nom.Data.Audit;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Recipe;
using Nom.Orch.Services.Support;

namespace Nom.Orch.Services
{
    /// <summary>
    /// The removal of recipes taken from forbidden sources (see <see cref="IRecipeRemovalService"/>). A retire is a soft
    /// delete of the recipe, its lines, its steps and its stored documents, all stamped with the run's stamp, so every surface
    /// and lane that reads live rows no longer sees them and the stored diet gate drops the recipe; one
    /// <see cref="RecipeRemovalEntity"/> per recipe journals it. A household's meal plan entries, favourites, cookbook
    /// entries and shopping-list items stay, served with the title, the source link and <see cref="RemovedNote"/>.
    /// </summary>
    public class RecipeRemovalService : IRecipeRemovalService
    {
        public const string RetireKind = "retire";
        public const string UndoKind = "undo";
        public const string PurgeKind = "purge";
        public const string RetireChangeType = "RecipeRetire";
        public const string RetireUndoChangeType = "RecipeRetireUndo";
        public const string PurgeChangeType = "RecipePurge";
        public const string ItemNameChangeType = "RecipeRetireItemName";
        public const string ItemEntityType = "ShoppingListItem";
        public const string FoodRetireChangeType = "RecipeRetireFood";
        public const string FoodEntityType = LineRelinkService.RetireEntityType;
        public const string RemovedNote = "This recipe is no longer available here.";
        public const string PurgedValuePrefix = "(purged with the recipe, sha256:";
        public const string PurgeWarning = "A purge deletes the source's wording and copies for good: it cannot be undone. Take a database backup first.";
        public const int BatchSize = 200;
        public const string TombstoneMark = " (deleted #";

        private static readonly SemaphoreSlim Gate = new(1, 1);
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
        private static readonly string[] RecipeAuditTypes = { ProseRecheckService.RecipeEntityType, "RecipeStep", "RecipeIngredient" };
        private static readonly string[] OwnChangeTypes = { RetireChangeType, RetireUndoChangeType, PurgeChangeType, FoodRetireChangeType };

        /// <summary>The recipe columns a purge clears; the title (<c>Name</c>), the address and the site stay.</summary>
        public static readonly string[] PurgedRecipeColumns =
        {
            nameof(RecipeEntity.Description), nameof(RecipeEntity.DescriptionNormalized), nameof(RecipeEntity.SourceImageUrl),
            nameof(RecipeEntity.SourceAttribution), nameof(RecipeEntity.SourceCredit), nameof(RecipeEntity.VettingIssues),
            nameof(RecipeEntity.RecipeYield), nameof(RecipeEntity.Image), nameof(RecipeEntity.ImageSourceName), nameof(RecipeEntity.ImageSourceUrl),
            nameof(RecipeEntity.ImageAuthor), nameof(RecipeEntity.ImageAuthorUrl), nameof(RecipeEntity.ImageLicense), nameof(RecipeEntity.ImageLicenseUrl),
            nameof(RecipeEntity.OrgUrl),
        };

        private readonly ApplicationDbContext _db;
        private readonly ILogger<RecipeRemovalService> _logger;

        public RecipeRemovalService(ApplicationDbContext db, ILogger<RecipeRemovalService> logger)
        {
            _db = db;
            _logger = logger;
        }

        internal Func<DateTime> Now { get; set; } = () => DateTime.UtcNow;

        internal int Batch { get; set; } = BatchSize;

        /// <summary>The address as removal compares it: the host without "www.", the path without a trailing slash, lowercased, no query or fragment; null for no address.</summary>
        public static string? UrlKey(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            var text = url.Trim();
            if (!text.Contains("://", StringComparison.Ordinal)) text = "https://" + text;
            if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)) return null;
            var host = uri.Host.TrimEnd('.').ToLowerInvariant();
            if (host.StartsWith("www.", StringComparison.Ordinal)) host = host[4..];
            var path = uri.AbsolutePath.TrimEnd('/').ToLowerInvariant();
            return host + path;
        }

        /// <summary>Why the address may not be taken again: a recipe from it was removed (retired, and not undone); null otherwise.</summary>
        public static async Task<string?> RefusalAsync(ApplicationDbContext db, string? url, CancellationToken cancellationToken = default)
        {
            if (UrlKey(url) is not { } key) return null;
            var removal = await db.RecipeRemovals.AsNoTracking()
                .Where(r => r.UrlKey == key && r.UndoneAtUtc == null)
                .OrderBy(r => r.Id)
                .FirstOrDefaultAsync(cancellationToken);
            return removal == null
                ? null
                : $"This address was removed: its source {removal.Host} forbids taking its recipes (recipe {removal.RecipeId.ToString(CultureInfo.InvariantCulture)}, removed {removal.RetiredAtUtc:yyyy-MM-dd}), so it is not taken again.";
        }

        /// <summary>The removed recipes among <paramref name="recipeIds"/> (retired and not undone), each with its title and source link, for the entries households still hold.</summary>
        public static async Task<Dictionary<long, RemovedRecipeModel>> RemovedAsync(ApplicationDbContext db, IEnumerable<long> recipeIds, CancellationToken cancellationToken = default)
        {
            var ids = recipeIds.Distinct().ToList();
            var found = new Dictionary<long, RemovedRecipeModel>();
            if (ids.Count == 0) return found;
            foreach (var chunk in ids.Chunk(5000))
            {
                foreach (var row in await db.RecipeRemovals.AsNoTracking()
                             .Where(r => chunk.Contains(r.RecipeId) && r.UndoneAtUtc == null)
                             .Select(r => new { r.RecipeId, r.Title, r.SourceUrl })
                             .ToListAsync(cancellationToken))
                    found[row.RecipeId] = new RemovedRecipeModel(row.RecipeId, row.Title, row.SourceUrl);
            }

            return found;
        }

        public async Task<RecipeRemovalReportModel> RetireAsync(long? sourceId, bool apply, long personId, string? reason, CancellationToken cancellationToken = default)
        {
            var now = Stamped(Now());
            var report = new RecipeRemovalReportModel { Stage = RetireKind, Applied = apply, Stamp = now, SourceId = sourceId };
            if (apply && !await Gate.WaitAsync(0, cancellationToken))
            {
                report.Refused = "A removal run is in progress.";
                return report;
            }

            try
            {
                var (run, close) = await UnfinishedAsync(RetireKind, sourceId, report, cancellationToken);
                if (report.Refused != null) return report;
                if (run != null) sourceId = run.ScrapingSourceId;
                if (apply && close != null)
                {
                    close.FinishedAtUtc = Now();
                    close.Reason = Clip($"{close.Reason} | closed {now:O}: its source no longer qualifies", 1023);
                    await _db.SaveChangesAsync(cancellationToken);
                }

                await RetirePlanAsync(report, sourceId, cancellationToken);
                if (report.Refused != null || !apply) return report;
                if (run == null)
                {
                    run = new RecipeRemovalRunEntity
                    {
                        Kind = RetireKind, Stamp = now, ScrapingSourceId = sourceId, PersonId = personId, StartedAtUtc = now, Planned = report.Recipes,
                        Reason = Clip(string.IsNullOrWhiteSpace(reason) ? "removed: taken from a forbidden source" : reason.Trim(), 1023),
                    };
                    _db.RecipeRemovalRuns.Add(run);
                    await _db.SaveChangesAsync(cancellationToken);
                }

                report.RunId = run.Id;
                var done = new HashSet<long>();
                var foodsRetired = 0;
                while (true)
                {
                    var hosts = await HostsAsync(sourceId, cancellationToken);
                    if (hosts.Refused != null) break;
                    var batch = await EligibleAsync(hosts.Hosts, Batch, cancellationToken);
                    if (batch.Count == 0 || batch.Any(b => done.Contains(b.Recipe.Id))) break;
                    await InBatchAsync(async () =>
                    {
                        var orphans = await OrphanFoodsAsync(batch.Select(b => b.Recipe.Id).ToList(), cancellationToken);
                        foreach (var (recipe, host) in batch)
                        {
                            await RetireOneAsync(recipe, host, run, personId, cancellationToken);
                            done.Add(recipe.Id);
                        }

                        await RetireFoodsAsync(orphans, run, personId, cancellationToken);
                        foodsRetired += orphans.Count;
                        run.Done += batch.Count;
                        await _db.SaveKeepingModifiedAsync(cancellationToken);
                    }, cancellationToken);
                    _db.ChangeTracker.Clear();
                    run = await _db.RecipeRemovalRuns.FirstAsync(r => r.Id == run.Id, cancellationToken);
                }

                run.FinishedAtUtc = Now();
                await _db.SaveChangesAsync(cancellationToken);
                report.Recipes = done.Count;
                report.Foods = foodsRetired;
                _logger.LogInformation("Recipe removal run {RunId} ({Stamp:O}): {Done} recipes retired from forbidden sources", run.Id, run.Stamp, run.Done);
                return report;
            }
            finally
            {
                if (apply) Gate.Release();
            }
        }

        /// <summary>
        /// The unfinished run of <paramref name="kind"/> an API restart cut off. It is resumed, under its stamp, only by an
        /// apply (or dry run) with the same scope; another scope is refused until it is finished, so a dry run and its apply
        /// always act on the scope asked for. A cut-off retire run whose source no longer qualifies is returned to be closed.
        /// </summary>
        private async Task<(RecipeRemovalRunEntity? Resume, RecipeRemovalRunEntity? Close)> UnfinishedAsync(string kind, long? sourceId, RecipeRemovalReportModel report, CancellationToken cancellationToken)
        {
            var run = await _db.RecipeRemovalRuns.Where(r => r.Kind == kind && r.FinishedAtUtc == null).OrderBy(r => r.Id).FirstOrDefaultAsync(cancellationToken);
            if (run == null) return (null, null);
            var runScope = await ScopeAsync(run.ScrapingSourceId, cancellationToken);
            var label = $"The {kind} run {run.Id.ToString(CultureInfo.InvariantCulture)} ({run.Stamp:O}, {runScope}) was cut off";
            if (kind == RetireKind && run.ScrapingSourceId != null && (await HostsAsync(run.ScrapingSourceId, cancellationToken)).Refused is { } gone)
            {
                report.Warning = $"{label} and no longer qualifies ({gone}). {(report.Applied ? "It is closed" : "The apply closes it")} as it stands; what it retired stays retired and is undone by its stamp.";
                return (null, run);
            }

            if (await ScopeAsync(sourceId, cancellationToken) != runScope)
            {
                var own = run.ScrapingSourceId is { } id ? $"sourceId={id.ToString(CultureInfo.InvariantCulture)}" : "no sourceId";
                report.Refused = $"{label}: finish it first by applying it with its own scope ({own}){(kind == RetireKind ? ", or undo it by its stamp" : string.Empty)}.";
                return (null, null);
            }

            report.Resumed = true;
            report.SourceId = run.ScrapingSourceId;
            report.Stamp = run.Stamp;
            report.RunId = run.Id;
            return (run, null);
        }

        private async Task<string> ScopeAsync(long? sourceId, CancellationToken cancellationToken) =>
            sourceId == null ? "every forbidden source" : await HostOfAsync(sourceId.Value, cancellationToken) ?? $"source {sourceId.Value.ToString(CultureInfo.InvariantCulture)}";

        private async Task<string?> HostOfAsync(long sourceId, CancellationToken cancellationToken)
        {
            var domain = await _db.ScrapingSources.AsNoTracking().Where(s => s.Id == sourceId).Select(s => s.Domain).FirstOrDefaultAsync(cancellationToken);
            return domain == null ? null : SourceAdmissionPolicy.NormalizeDomain(domain) ?? domain;
        }

        private async Task RetireOneAsync(RecipeEntity recipe, HostSource host, RecipeRemovalRunEntity run, long personId, CancellationToken cancellationToken)
        {
            var stamp = run.Stamp;
            var lines = await _db.RecipeIngredients.IgnoreQueryFilters().Where(l => l.RecipeId == recipe.Id && !l.IsDeleted).ToListAsync(cancellationToken);
            var steps = await _db.RecipeSteps.IgnoreQueryFilters().Where(s => s.RecipeId == recipe.Id && !s.IsDeleted).ToListAsync(cancellationToken);
            var documents = await _db.ScrapedDocuments.IgnoreQueryFilters().Where(d => d.RecipeId == recipe.Id && !d.IsDeleted).ToListAsync(cancellationToken);
            foreach (var row in lines.Cast<BaseEntity>().Concat(steps).Concat(documents)) SoftDelete(row, stamp, personId);
            var wasDeleted = recipe.IsDeleted;
            if (!wasDeleted) SoftDelete(recipe, stamp, personId);
            await DropDietRowsAsync(recipe.Id, cancellationToken);

            var items = await _db.ShoppingListItems.IgnoreQueryFilters().Where(i => i.RecipeId == recipe.Id && !i.IsDeleted).ToListAsync(cancellationToken);
            if (items.Count > 0)
            {
                var foods = await _db.Ingredients.IgnoreQueryFilters().AsNoTracking()
                    .Where(i => items.Select(x => x.IngredientId).Contains(i.Id))
                    .ToDictionaryAsync(i => i.Id, cancellationToken);
                foreach (var item in items)
                {
                    var food = item.IngredientId is { } id ? foods.GetValueOrDefault(id) : null;
                    var line = lines.FirstOrDefault(l => l.IngredientId == item.IngredientId);
                    var name = ItemName(food, line?.RawLine);
                    if (string.Equals(name, item.Name, StringComparison.Ordinal)) continue;
                    _db.AuditLogEntries.Add(Entry(ItemEntityType, item.Id, ItemNameChangeType, nameof(item.Name), item.Name, name, stamp, personId));
                    item.Name = name;
                }
            }

            _db.RecipeRemovals.Add(new RecipeRemovalEntity
            {
                RecipeId = recipe.Id,
                RunId = run.Id,
                ScrapingSourceId = host.SourceId,
                Host = host.Host,
                SourceUrl = Clip(recipe.SourceUrl, 2047) ?? string.Empty,
                UrlKey = Clip(UrlKey(recipe.SourceUrl), 2047) ?? string.Empty,
                Title = Clip(recipe.Name, 1023) ?? string.Empty,
                RightsClass = SourceRightsRule.Forbidden,
                WasDeleted = wasDeleted,
                RetiredAtUtc = stamp,
                RetiredByPersonId = personId,
                LinesRetired = lines.Count,
                StepsRetired = steps.Count,
                DocumentsRetired = documents.Count,
            });
            _db.AuditLogEntries.Add(Entry(ProseRecheckService.RecipeEntityType, recipe.Id, RetireChangeType, nameof(BaseEntity.IsDeleted),
                wasDeleted ? "true" : "false", $"true | retired: taken from {host.Host} (forbidden) | run {run.Id.ToString(CultureInfo.InvariantCulture)}", stamp, personId));
        }

        private async Task<RecipeRemovalReportModel> RetirePlanAsync(RecipeRemovalReportModel report, long? sourceId, CancellationToken cancellationToken)
        {
            var hosts = await HostsAsync(sourceId, cancellationToken);
            if (hosts.Refused != null)
            {
                report.Refused = hosts.Refused;
                return report;
            }

            var ids = new List<long>();
            foreach (var host in hosts.Hosts)
            {
                var recipes = await Candidates(host).Select(r => new { r.Id, r.IsDeleted, r.CurationStatusId }).ToListAsync(cancellationToken);
                var model = Model(host);
                model.Recipes = recipes.Count;
                model.AlreadyDeleted = recipes.Count(r => r.IsDeleted);
                model.Approved = recipes.Count(r => r.CurationStatusId == (long)CurationStatusEnum.Curated);
                await HoldingsAsync(model, recipes.Select(r => r.Id).ToList(), recipes.Where(r => r.CurationStatusId == (long)CurationStatusEnum.Curated).Select(r => r.Id).ToList(), cancellationToken);
                report.Sources.Add(model);
                ids.AddRange(recipes.Select(r => r.Id));
            }

            report.Recipes = ids.Count;
            report.Foods = (await OrphanFoodsAsync(ids, cancellationToken)).Count;
            report.AlreadyDone = await _db.RecipeRemovals.AsNoTracking().CountAsync(r => r.UndoneAtUtc == null
                && (sourceId == null || hosts.Hosts.Select(h => h.Host).Contains(r.Host)), cancellationToken);
            report.RecipeIds = ids.OrderBy(id => id).Take(500).ToList();
            return report;
        }

        public async Task<RecipeRemovalReportModel> UndoRetireAsync(DateTime stamp, bool apply, long personId, CancellationToken cancellationToken = default)
        {
            var now = Stamped(Now());
            var report = new RecipeRemovalReportModel { Stage = RetireKind + "-" + UndoKind, Applied = apply, Stamp = now };
            var run = await _db.RecipeRemovalRuns.AsNoTracking()
                .Where(r => r.Kind == RetireKind && r.Stamp >= stamp.AddMilliseconds(-1) && r.Stamp <= stamp.AddMilliseconds(1))
                .OrderBy(r => r.Id).FirstOrDefaultAsync(cancellationToken);
            if (run == null)
            {
                report.Refused = $"No retire run has the stamp {stamp:O}.";
                return report;
            }

            report.RunId = run.Id;
            report.SourceId = run.ScrapingSourceId;
            if (apply && !await Gate.WaitAsync(0, cancellationToken))
            {
                report.Refused = "A removal run is in progress.";
                return report;
            }

            try
            {
                var rows = await _db.RecipeRemovals.AsNoTracking().Where(r => r.RunId == run.Id).OrderBy(r => r.Id).ToListAsync(cancellationToken);
                report.AlreadyDone = rows.Count(r => r.UndoneAtUtc != null);
                foreach (var row in rows.Where(r => r.UndoneAtUtc == null && r.PurgedAtUtc != null)) Skip(report, "purged: its source wording is deleted for good, so it cannot be put back");
                var undoable = rows.Where(r => r.UndoneAtUtc == null && r.PurgedAtUtc == null).ToList();
                report.Recipes = undoable.Count;
                report.RecipeIds = undoable.Select(r => r.RecipeId).OrderBy(id => id).Take(500).ToList();
                foreach (var group in undoable.GroupBy(r => r.ScrapingSourceId))
                    report.Sources.Add(new RecipeRemovalSourceModel { SourceId = group.Key, Domain = group.First().Host, RightsClass = group.First().RightsClass, Recipes = group.Count() });
                if (run.FinishedAtUtc == null)
                    report.Warning = $"The retire run {run.Id.ToString(CultureInfo.InvariantCulture)} was cut off; {(apply ? "the undo closed it" : "the undo closes it")}, so it is not resumed.";
                if (!apply)
                {
                    report.Foods = (await RetiredFoodsAsync(undoable.Select(r => r.RecipeId).ToList(), run.Stamp, cancellationToken)).Count;
                    return report;
                }

                if (run.FinishedAtUtc == null)
                {
                    var cut = await _db.RecipeRemovalRuns.FirstAsync(r => r.Id == run.Id, cancellationToken);
                    cut.FinishedAtUtc = Now();
                    cut.Reason = Clip($"{cut.Reason} | closed {now:O}: undone by its stamp", 1023);
                    await _db.SaveChangesAsync(cancellationToken);
                }

                var undoRun = new RecipeRemovalRunEntity
                {
                    Kind = UndoKind, Stamp = now, UndoesStamp = run.Stamp, ScrapingSourceId = run.ScrapingSourceId, PersonId = personId, StartedAtUtc = now, Planned = undoable.Count,
                };
                _db.RecipeRemovalRuns.Add(undoRun);
                await _db.SaveChangesAsync(cancellationToken);
                foreach (var chunk in undoable.Chunk(Batch))
                {
                    await InBatchAsync(async () =>
                    {
                        var foods = await RetiredFoodsAsync(chunk.Select(r => r.RecipeId).ToList(), run.Stamp, cancellationToken);
                        foreach (var row in chunk) await UndoOneAsync(row.Id, run.Stamp, now, personId, cancellationToken);
                        foreach (var food in await _db.Ingredients.IgnoreQueryFilters().Where(i => foods.Contains(i.Id)).ToListAsync(cancellationToken))
                        {
                            Restore(food);
                            _db.AuditLogEntries.Add(Entry(FoodEntityType, food.Id, FoodRetireChangeType, nameof(BaseEntity.IsDeleted), "true",
                                $"false | undo of retire run {run.Id.ToString(CultureInfo.InvariantCulture)}", now, personId));
                        }

                        report.Foods += foods.Count;
                        var tracked = await _db.RecipeRemovalRuns.FirstAsync(r => r.Id == undoRun.Id, cancellationToken);
                        tracked.Done += chunk.Length;
                        await _db.SaveKeepingModifiedAsync(cancellationToken);
                    }, cancellationToken);
                    _db.ChangeTracker.Clear();
                }

                var finished = await _db.RecipeRemovalRuns.FirstAsync(r => r.Id == undoRun.Id, cancellationToken);
                finished.FinishedAtUtc = Now();
                await _db.SaveChangesAsync(cancellationToken);
                return report;
            }
            finally
            {
                if (apply) Gate.Release();
            }
        }

        private async Task UndoOneAsync(long removalId, DateTime stamp, DateTime now, long personId, CancellationToken cancellationToken)
        {
            var removal = await _db.RecipeRemovals.FirstAsync(r => r.Id == removalId, cancellationToken);
            if (removal.UndoneAtUtc != null || removal.PurgedAtUtc != null) return;
            var id = removal.RecipeId;
            var recipe = await _db.Recipes.IgnoreQueryFilters().FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
            if (recipe != null && !removal.WasDeleted && recipe.IsDeleted && recipe.DeletedAt == stamp) Restore(recipe);
            foreach (var row in await _db.RecipeIngredients.IgnoreQueryFilters().Where(l => l.RecipeId == id && l.IsDeleted && l.DeletedAt == stamp).ToListAsync(cancellationToken)) Restore(row);
            foreach (var row in await _db.RecipeSteps.IgnoreQueryFilters().Where(s => s.RecipeId == id && s.IsDeleted && s.DeletedAt == stamp).ToListAsync(cancellationToken)) Restore(row);
            foreach (var row in await _db.ScrapedDocuments.IgnoreQueryFilters().Where(d => d.RecipeId == id && d.IsDeleted && d.DeletedAt == stamp).ToListAsync(cancellationToken)) Restore(row);
            var items = await _db.ShoppingListItems.IgnoreQueryFilters().Where(i => i.RecipeId == id).ToListAsync(cancellationToken);
            if (items.Count > 0)
            {
                var itemIds = items.Select(i => i.Id).ToList();
                var renames = await _db.AuditLogEntries.AsNoTracking()
                    .Where(a => a.EntityType == ItemEntityType && a.ChangeType == ItemNameChangeType && itemIds.Contains(a.EntityId) && a.Timestamp == stamp)
                    .ToListAsync(cancellationToken);
                foreach (var rename in renames)
                {
                    var item = items.First(i => i.Id == rename.EntityId);
                    if (item.Name == rename.NewValue && rename.OldValue != null) item.Name = rename.OldValue;
                }
            }

            removal.UndoneAtUtc = now;
            removal.UndoneByPersonId = personId;
            _db.AuditLogEntries.Add(Entry(ProseRecheckService.RecipeEntityType, id, RetireUndoChangeType, nameof(BaseEntity.IsDeleted),
                "true", $"{(removal.WasDeleted ? "true" : "false")} | undo of retire run {removal.RunId.ToString(CultureInfo.InvariantCulture)}", now, personId));
        }

        public async Task<RecipeRemovalReportModel> PurgeAsync(long? sourceId, bool apply, bool backupTaken, long personId, CancellationToken cancellationToken = default)
        {
            var now = Stamped(Now());
            var report = new RecipeRemovalReportModel { Stage = PurgeKind, Applied = apply, Stamp = now, SourceId = sourceId, Warning = PurgeWarning };
            if (apply && !backupTaken)
            {
                report.Refused = "A purge cannot be undone. Take a database backup first, then apply with backupTaken=true.";
                return report;
            }

            if (apply && !await Gate.WaitAsync(0, cancellationToken))
            {
                report.Refused = "A removal run is in progress.";
                return report;
            }

            try
            {
                var (run, _) = await UnfinishedAsync(PurgeKind, sourceId, report, cancellationToken);
                if (report.Refused != null) return report;
                if (run != null) sourceId = run.ScrapingSourceId;
                var host = sourceId is { } id ? await HostOfAsync(id, cancellationToken) : null;
                if (sourceId != null && host == null)
                {
                    report.Refused = $"No source {sourceId.Value.ToString(CultureInfo.InvariantCulture)}.";
                    return report;
                }

                var due = await PurgeDue(host).OrderBy(r => r.Id).ToListAsync(cancellationToken);
                report.Recipes = due.Count;
                report.AlreadyDone = await _db.RecipeRemovals.AsNoTracking().CountAsync(r => r.UndoneAtUtc == null && r.PurgedAtUtc != null && (host == null || r.Host == host), cancellationToken);
                report.RecipeIds = due.Select(r => r.RecipeId).OrderBy(id => id).Take(500).ToList();
                foreach (var group in due.GroupBy(r => r.ScrapingSourceId))
                    report.Sources.Add(await PurgeSourceAsync(group.ToList(), cancellationToken));
                report.Targets = await PurgeTargetsAsync(due.Select(r => r.RecipeId).ToList(), cancellationToken);
                if (!apply) return report;

                if (run == null)
                {
                    run = new RecipeRemovalRunEntity { Kind = PurgeKind, Stamp = now, ScrapingSourceId = sourceId, PersonId = personId, StartedAtUtc = now, Planned = due.Count };
                    _db.RecipeRemovalRuns.Add(run);
                    await _db.SaveChangesAsync(cancellationToken);
                }

                report.RunId = run.Id;
                var runId = run.Id;
                var stamp = run.Stamp;
                foreach (var chunk in due.Chunk(Batch))
                {
                    await InBatchAsync(async () =>
                    {
                        var plan = await WordingPlanAsync(chunk.Select(r => r.RecipeId).ToList(), cancellationToken);
                        foreach (var removal in chunk) await PurgeOneAsync(removal.Id, runId, stamp, personId, cancellationToken);
                        await PurgeWordingsAsync(plan, stamp, personId, cancellationToken);
                        var tracked = await _db.RecipeRemovalRuns.FirstAsync(r => r.Id == runId, cancellationToken);
                        tracked.Done += chunk.Length;
                        await _db.SaveHardDeletesAsync(cancellationToken);
                    }, cancellationToken);
                    _db.ChangeTracker.Clear();
                }

                var finished = await _db.RecipeRemovalRuns.FirstAsync(r => r.Id == runId, cancellationToken);
                finished.FinishedAtUtc = Now();
                await _db.SaveChangesAsync(cancellationToken);
                _logger.LogWarning("Recipe purge run {RunId} ({Stamp:O}): the stored source wording of {Count} retired recipes deleted for good", runId, stamp, due.Count);
                return report;
            }
            finally
            {
                if (apply) Gate.Release();
            }
        }

        private IQueryable<RecipeRemovalEntity> PurgeDue(string? host) =>
            _db.RecipeRemovals.AsNoTracking().Where(r => r.UndoneAtUtc == null && r.PurgedAtUtc == null && (host == null || r.Host == host));

        private async Task<List<RecipeRemovalTargetModel>> PurgeTargetsAsync(List<long> ids, CancellationToken cancellationToken)
        {
            var targets = new List<RecipeRemovalTargetModel>();
            async Task Count(string table, string columns, string action, Func<long[], Task<int>> rows)
            {
                var total = 0;
                foreach (var chunk in ids.Chunk(5000)) total += await rows(chunk);
                targets.Add(new RecipeRemovalTargetModel { Table = table, Columns = columns, Action = action, Rows = total });
            }

            await Count("recipe.Recipe", string.Join(", ", PurgedRecipeColumns), "cleared (Name, SourceUrl and SourceSite stay)",
                c => _db.Recipes.IgnoreQueryFilters().CountAsync(r => c.Contains(r.Id), cancellationToken));
            await Count("recipe.RecipeStep", "Description, Summary (whole rows)", "deleted", c => _db.RecipeSteps.IgnoreQueryFilters().CountAsync(r => c.Contains(r.RecipeId), cancellationToken));
            await Count("recipe.RecipeIngredient", "RawLine, LineKind (whole rows)", "deleted", c => _db.RecipeIngredients.IgnoreQueryFilters().CountAsync(r => c.Contains(r.RecipeId), cancellationToken));
            await Count("recipe.ScrapedDocument", "RawJsonLd: stored page data, records, held and reduced prose, the prose stock index's documents (whole rows)", "deleted",
                c => _db.ScrapedDocuments.IgnoreQueryFilters().CountAsync(r => c.Contains(r.RecipeId), cancellationToken));
            await Count("recipe.RecipeReprocessJournal", "OldSteps, NewSteps, line texts (whole rows)", "deleted", c => _db.RecipeReprocessJournals.IgnoreQueryFilters().CountAsync(r => c.Contains(r.RecipeId), cancellationToken));
            await Count("recipe.LineGuardHold", "RawLine, StoredFood, RebuiltFood (whole rows)", "deleted", c => _db.LineGuardHolds.IgnoreQueryFilters().CountAsync(r => c.Contains(r.RecipeId), cancellationToken));
            await Count("recipe.ReprocessCandidate", "whole rows", "deleted", c => _db.ReprocessCandidates.IgnoreQueryFilters().CountAsync(r => c.Contains(r.RecipeId), cancellationToken));
            await Count("recipe.RecipeImageCandidate", "image addresses and credits (whole rows)", "deleted", c => _db.RecipeImageCandidates.IgnoreQueryFilters().CountAsync(r => c.Contains(r.RecipeId), cancellationToken));
            await Count("recipe.RecipeAsset", "FileData, FilePath, Name, Description: any fetched image or file (whole rows)", "deleted", c => _db.RecipeAssets.IgnoreQueryFilters().CountAsync(r => c.Contains(r.RecipeId), cancellationToken));
            await Count("recipe.RecipeAugmentation", "Instructions, FlavorEffect (whole rows)", "deleted", c => _db.RecipeAugmentations.IgnoreQueryFilters().CountAsync(r => c.Contains(r.RecipeId), cancellationToken));
            await Count("recipe.RecipeSubstitution", "whole rows, with their step effects", "deleted", c => _db.RecipeSubstitutions.IgnoreQueryFilters().CountAsync(r => c.Contains(r.RecipeId), cancellationToken));
            await Count("plan.DietRecipeFlag, plan.DietRecipeState", "the stored diet gate's rows (whole rows)", "deleted",
                async c => await _db.DietRecipeFlags.CountAsync(r => c.Contains(r.RecipeId), cancellationToken) + await _db.DietRecipeStates.CountAsync(r => c.Contains(r.RecipeId), cancellationToken));
            await Count("audit.AuditLogEntry", "OldValue, NewValue of the recipe's, its steps', its lines' and its documents' journal rows and of its shopping items' renames",
                "replaced by " + PurgedValuePrefix + "...)", async c =>
                {
                    var docs = await _db.ScrapedDocuments.IgnoreQueryFilters().Where(d => c.Contains(d.RecipeId)).Select(d => d.Id).ToListAsync(cancellationToken);
                    var items = await _db.ShoppingListItems.IgnoreQueryFilters().Where(i => i.RecipeId != null && c.Contains(i.RecipeId.Value)).Select(i => i.Id).ToListAsync(cancellationToken);
                    return await AuditRows(c, docs, items).CountAsync(cancellationToken);
                });
            await Count("curation.LaneDecision", "SignalsJson, VerdictsJson, AuditJson of decisions about the recipe", "cleared", c =>
            {
                var subjects = c.Select(id => "recipe:" + id.ToString(CultureInfo.InvariantCulture)).ToList();
                return _db.LaneDecisions.IgnoreQueryFilters().CountAsync(d => subjects.Contains(d.Subject), cancellationToken);
            });
            await Count("shopping.ShoppingListItem", "Name: served by its catalog food or its own food words (rows stay with the household)", "renamed where it differs",
                c => _db.ShoppingListItems.IgnoreQueryFilters().CountAsync(i => i.RecipeId != null && c.Contains(i.RecipeId.Value), cancellationToken));

            var plan = await WordingPlanAsync(ids, cancellationToken);
            void Add(string table, string columns, string action, int rows) => targets.Add(new RecipeRemovalTargetModel { Table = table, Columns = columns, Action = action, Rows = rows });
            Add("recipe.Ingredient", "Name, NameNormalized, PluralName, PluralNameNormalized, Description of the foods named from the purged lines that no other recipe uses",
                "renamed by their food words where a household or another food holds them, otherwise cleared and the food retired", plan.Foods.Count);
            Add("recipe.IngredientAlias", "AliasName of those foods, the aliases a relink gave another food under the name of one of them, and any alias whose name is a purged line's wording that no other recipe uses (whole rows)", "deleted",
                await _db.IngredientAliases.IgnoreQueryFilters().CountAsync(a => plan.Foods.Contains(a.IngredientId) || (a.SourceContext != null && plan.RetiredContexts.Contains(a.SourceContext)), cancellationToken)
                + (await WordedAliasesAsync(plan, cancellationToken)).Count);
            Add("recipe.IngredientWording, recipe.IngredientWordingRejection", "Wording of deleted rows and rejections on those foods (whole rows)", "deleted",
                await _db.IngredientWordings.IgnoreQueryFilters().CountAsync(w => w.IsDeleted && w.IngredientId != null && plan.Foods.Contains(w.IngredientId.Value), cancellationToken)
                + await _db.IngredientWordingRejections.IgnoreQueryFilters().CountAsync(w => w.IngredientId != null && plan.Foods.Contains(w.IngredientId.Value), cancellationToken));
            Add("curation.FoodCatalogProposal", "CurrentValue, ProposedValue, Reason of proposals on those foods or keyed by a purged line's wording that no other recipe uses",
                "replaced by " + PurgedValuePrefix + "...)", plan.Proposals.Count);
            Add("audit.AuditLogEntry (foods, proposals)", "OldValue, NewValue of those foods' and proposals' journal rows", "replaced by " + PurgedValuePrefix + "...)",
                await FoodAuditRows(plan).CountAsync(cancellationToken));
            Add("curation.LaneDecision (wordings)", "Subject, SignalsJson, VerdictsJson, AuditJson of decisions about those wordings", "cleared (the subject replaced by " + PurgedValuePrefix + "...))",
                await _db.LaneDecisions.IgnoreQueryFilters().CountAsync(d => plan.Subjects.Contains(d.Subject), cancellationToken));
            return targets;
        }

        private IQueryable<AuditLogEntryEntity> AuditRows(long[] recipeIds, List<long> documentIds, List<long> itemIds) => AuditRowsOf(_db, recipeIds, documentIds, itemIds);

        /// <summary>The journal rows that can hold a recipe's source text: its own, its steps', its lines' and its documents' rows, and its shopping items' renames; the removal's own rows excepted.</summary>
        internal static IQueryable<AuditLogEntryEntity> AuditRowsOf(ApplicationDbContext db, long[] recipeIds, List<long> documentIds, List<long> itemIds) =>
            db.AuditLogEntries.Where(a => !OwnChangeTypes.Contains(a.ChangeType)
                && ((RecipeAuditTypes.Contains(a.EntityType) && recipeIds.Contains(a.EntityId))
                    || (a.EntityType == RecipeSourceService.DocumentEntityType && documentIds.Contains(a.EntityId))
                    || (a.EntityType == ItemEntityType && a.ChangeType == ItemNameChangeType && itemIds.Contains(a.EntityId))));

        private async Task PurgeOneAsync(long removalId, long runId, DateTime stamp, long personId, CancellationToken cancellationToken)
        {
            var removal = await _db.RecipeRemovals.FirstAsync(r => r.Id == removalId, cancellationToken);
            if (removal.UndoneAtUtc != null || removal.PurgedAtUtc != null) return;
            var id = removal.RecipeId;
            var ids = new[] { id };
            var summary = new SortedDictionary<string, int>(StringComparer.Ordinal);
            void Count(string table, int rows)
            {
                if (rows > 0) summary[table] = summary.GetValueOrDefault(table) + rows;
            }

            if (await _db.Recipes.IgnoreQueryFilters().FirstOrDefaultAsync(r => r.Id == id, cancellationToken) is { } recipe)
            {
                var entry = _db.Entry(recipe);
                var cleared = 0;
                foreach (var column in PurgedRecipeColumns)
                {
                    var property = entry.Property(column);
                    if (property.CurrentValue == null) continue;
                    property.CurrentValue = null;
                    cleared++;
                }

                Count("recipe.Recipe columns", cleared);
            }

            var documents = await _db.ScrapedDocuments.IgnoreQueryFilters().Where(d => d.RecipeId == id).ToListAsync(cancellationToken);
            var lines = await _db.RecipeIngredients.IgnoreQueryFilters().Where(r => r.RecipeId == id).ToListAsync(cancellationToken);
            var items = await _db.ShoppingListItems.IgnoreQueryFilters().Where(i => i.RecipeId == id).ToListAsync(cancellationToken);
            foreach (var item in items)
            {
                var food = item.IngredientId is { } foodId ? await _db.Ingredients.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(i => i.Id == foodId, cancellationToken) : null;
                var line = lines.FirstOrDefault(l => l.IngredientId == item.IngredientId);
                var name = ItemName(food, line?.RawLine);
                if (string.Equals(name, item.Name, StringComparison.Ordinal)) continue;
                item.Name = name;
                Count("shopping.ShoppingListItem names", 1);
            }

            var documentIds = documents.Select(d => d.Id).ToList();
            var itemIds = items.Select(i => i.Id).ToList();
            foreach (var row in await AuditRows(ids, documentIds, itemIds).ToListAsync(cancellationToken))
            {
                var changed = false;
                if (row.OldValue != null && !row.OldValue.StartsWith(PurgedValuePrefix, StringComparison.Ordinal))
                {
                    row.OldValue = PurgedValue(row.OldValue);
                    changed = true;
                }

                if (row.NewValue != null && !row.NewValue.StartsWith(PurgedValuePrefix, StringComparison.Ordinal))
                {
                    row.NewValue = PurgedValue(row.NewValue);
                    changed = true;
                }

                if (changed) Count("audit.AuditLogEntry", 1);
            }

            var subject = "recipe:" + id.ToString(CultureInfo.InvariantCulture);
            foreach (var decision in await _db.LaneDecisions.IgnoreQueryFilters().Where(d => d.Subject == subject).ToListAsync(cancellationToken))
            {
                decision.SignalsJson = null;
                decision.VerdictsJson = null;
                decision.AuditJson = null;
                Count("curation.LaneDecision", 1);
            }

            Delete("recipe.ScrapedDocument", documents);
            Delete("recipe.RecipeIngredient", lines);
            Delete("recipe.RecipeStep", await _db.RecipeSteps.IgnoreQueryFilters().Where(r => r.RecipeId == id).ToListAsync(cancellationToken));
            Delete("recipe.RecipeReprocessJournal", await _db.RecipeReprocessJournals.IgnoreQueryFilters().Where(r => r.RecipeId == id).ToListAsync(cancellationToken));
            Delete("recipe.LineGuardHold", await _db.LineGuardHolds.IgnoreQueryFilters().Where(r => r.RecipeId == id).ToListAsync(cancellationToken));
            Delete("recipe.ReprocessCandidate", await _db.ReprocessCandidates.IgnoreQueryFilters().Where(r => r.RecipeId == id).ToListAsync(cancellationToken));
            Delete("recipe.RecipeImageCandidate", await _db.RecipeImageCandidates.IgnoreQueryFilters().Where(r => r.RecipeId == id).ToListAsync(cancellationToken));
            Delete("recipe.RecipeAsset", await _db.RecipeAssets.IgnoreQueryFilters().Where(r => r.RecipeId == id).ToListAsync(cancellationToken));
            Delete("recipe.RecipeAugmentation", await _db.RecipeAugmentations.IgnoreQueryFilters().Where(r => r.RecipeId == id).ToListAsync(cancellationToken));
            var substitutions = await _db.RecipeSubstitutions.IgnoreQueryFilters().Where(r => r.RecipeId == id).ToListAsync(cancellationToken);
            var substitutionIds = substitutions.Select(s => s.Id).ToList();
            Delete("recipe.RecipeSubstitutionStepEffect", await _db.RecipeSubstitutionStepEffects.IgnoreQueryFilters().Where(e => substitutionIds.Contains(e.RecipeSubstitutionId)).ToListAsync(cancellationToken));
            Delete("recipe.RecipeSubstitution", substitutions);
            var (flags, states) = await DropDietRowsAsync(id, cancellationToken);
            Count("plan.DietRecipeFlag", flags);
            Count("plan.DietRecipeState", states);

            removal.PurgedAtUtc = stamp;
            removal.PurgedByPersonId = personId;
            removal.PurgeRunId = runId;
            removal.PurgeSummary = JsonSerializer.Serialize(summary, JsonOptions);
            _db.AuditLogEntries.Add(Entry(ProseRecheckService.RecipeEntityType, id, PurgeChangeType, "SourceWording",
                null, $"purged | run {runId.ToString(CultureInfo.InvariantCulture)} | {removal.PurgeSummary}", stamp, personId));

            void Delete<T>(string table, List<T> rows) where T : class
            {
                if (rows.Count == 0) return;
                _db.RemoveRange(rows);
                Count(table, rows.Count);
            }
        }

        public async Task<RecipeRemovalStatusModel> StatusAsync(CancellationToken cancellationToken = default)
        {
            var status = new RecipeRemovalStatusModel();
            var removals = await _db.RecipeRemovals.AsNoTracking().Where(r => r.UndoneAtUtc == null)
                .GroupBy(r => r.Host)
                .Select(g => new { Host = g.Key, Retired = g.Count(), Purged = g.Count(r => r.PurgedAtUtc != null) })
                .ToListAsync(cancellationToken);
            status.Retired = removals.Sum(r => r.Retired);
            status.Purged = removals.Sum(r => r.Purged);
            var sources = await _db.ScrapingSources.AsNoTracking()
                .Where(s => s.Kind == ScrapingSourceEntity.WebKind)
                .OrderBy(s => s.Id)
                .ToListAsync(cancellationToken);
            foreach (var group in sources.GroupBy(s => SourceAdmissionPolicy.NormalizeDomain(s.Domain) ?? s.Domain))
            {
                var first = group.First();
                var classes = group.Select(SourceRightsService.EffectiveClass).Distinct().ToList();
                var removed = removals.FirstOrDefault(r => r.Host == group.Key);
                var barred = classes.Any(c => c is SourceRightsRule.Forbidden or SourceRightsRule.PermissionNeeded);
                if (!barred && removed == null) continue;
                status.BySource.Add(new RecipeRemovalFiguresModel
                {
                    SourceId = first.Id,
                    Domain = first.Domain,
                    RightsClass = classes.Count == 1 ? classes[0] : string.Join(" / ", classes),
                    Removable = classes.All(c => c == SourceRightsRule.Forbidden),
                    Taken = await SourceAdmissionService.RecipesFrom(_db.Recipes.AsNoTracking(), first.Domain).CountAsync(cancellationToken),
                    Retired = removed?.Retired ?? 0,
                    Purged = removed?.Purged ?? 0,
                });
            }

            var runs = await _db.RecipeRemovalRuns.AsNoTracking().OrderByDescending(r => r.Id).Take(20).ToListAsync(cancellationToken);
            foreach (var run in runs)
            {
                var model = RunModel(run);
                if (run.Kind == RetireKind)
                    model.Undoable = await _db.RecipeRemovals.AsNoTracking().CountAsync(r => r.RunId == run.Id && r.UndoneAtUtc == null && r.PurgedAtUtc == null, cancellationToken);
                status.Runs.Add(model);
            }

            status.Unfinished = status.Runs.FirstOrDefault(r => r.FinishedAtUtc == null);
            return status;
        }

        /// <summary>The removed recipes of one host (retired and not undone), counted per source in the rights report.</summary>
        public static async Task<(int Retired, int Purged)> FiguresAsync(ApplicationDbContext db, string domain, CancellationToken cancellationToken = default)
        {
            var host = SourceAdmissionPolicy.NormalizeDomain(domain) ?? domain;
            var rows = await db.RecipeRemovals.AsNoTracking().Where(r => r.Host == host && r.UndoneAtUtc == null)
                .Select(r => r.PurgedAtUtc != null).ToListAsync(cancellationToken);
            return (rows.Count, rows.Count(p => p));
        }

        private static RecipeRemovalRunModel RunModel(RecipeRemovalRunEntity run) => new()
        {
            Id = run.Id, Kind = run.Kind, Stamp = run.Stamp, SourceId = run.ScrapingSourceId, UndoesStamp = run.UndoesStamp, PersonId = run.PersonId,
            Reason = run.Reason, Planned = run.Planned, Done = run.Done, Skipped = run.Skipped, StartedAtUtc = run.StartedAtUtc, FinishedAtUtc = run.FinishedAtUtc,
        };

        private sealed record HostSource(long SourceId, string Host, string Domain, string? RightsClass, string? RightsQuote, string? RightsQuoteUrl);

        private sealed record HostList(List<HostSource> Hosts, string? Refused);

        /// <summary>
        /// The hosts whose recipes qualify: every web row of the host has the effective class forbidden (a written permission or
        /// a lifted class on any row takes the host out); with <paramref name="sourceId"/> only that source's host, refused when it
        /// does not qualify.
        /// </summary>
        private async Task<HostList> HostsAsync(long? sourceId, CancellationToken cancellationToken)
        {
            var sources = await _db.ScrapingSources.AsNoTracking().Where(s => s.Kind == ScrapingSourceEntity.WebKind).OrderBy(s => s.Id).ToListAsync(cancellationToken);
            var hosts = new List<HostSource>();
            foreach (var group in sources.GroupBy(s => SourceAdmissionPolicy.NormalizeDomain(s.Domain) ?? s.Domain))
            {
                if (!group.All(s => SourceRightsService.EffectiveClass(s) == SourceRightsRule.Forbidden)) continue;
                var first = group.First();
                hosts.Add(new HostSource(first.Id, group.Key, first.Domain, SourceRightsService.EffectiveClass(first), first.RightsQuote, first.RightsQuoteUrl));
            }

            if (sourceId == null) return new HostList(hosts, null);
            var source = sources.FirstOrDefault(s => s.Id == sourceId);
            if (source == null) return new HostList(new List<HostSource>(), $"No web source {sourceId.Value.ToString(CultureInfo.InvariantCulture)}.");
            var host = SourceAdmissionPolicy.NormalizeDomain(source.Domain) ?? source.Domain;
            var match = hosts.FirstOrDefault(h => h.Host == host);
            if (match != null) return new HostList(new List<HostSource> { match }, null);
            var classes = sources.Where(s => (SourceAdmissionPolicy.NormalizeDomain(s.Domain) ?? s.Domain) == host).Select(s => SourceRightsService.EffectiveClass(s) ?? "unclassed").Distinct();
            return new HostList(new List<HostSource>(), $"{source.Domain} is not forbidden (its effective class is {string.Join(" / ", classes)}): only recipes of forbidden sources are removed.");
        }

        private IQueryable<RecipeEntity> Candidates(HostSource host) => CandidatesOf(_db, host.Domain);

        /// <summary>The recipes taken from the domain (with or without "www.", deleted ones included) that are not removed now.</summary>
        internal static IQueryable<RecipeEntity> CandidatesOf(ApplicationDbContext db, string domain) =>
            SourceAdmissionService.RecipesFrom(db.Recipes.IgnoreQueryFilters().AsNoTracking(), domain)
                .Where(r => !db.RecipeRemovals.Any(x => x.RecipeId == r.Id && x.UndoneAtUtc == null));

        private async Task<List<(RecipeEntity Recipe, HostSource Host)>> EligibleAsync(List<HostSource> hosts, int take, CancellationToken cancellationToken)
        {
            var batch = new List<(RecipeEntity Recipe, HostSource Host)>();
            foreach (var host in hosts)
            {
                if (batch.Count >= take) break;
                var recipes = await SourceAdmissionService.RecipesFrom(_db.Recipes.IgnoreQueryFilters(), host.Domain)
                    .Where(r => !_db.RecipeRemovals.Any(x => x.RecipeId == r.Id && x.UndoneAtUtc == null))
                    .OrderBy(r => r.Id)
                    .Take(take - batch.Count)
                    .ToListAsync(cancellationToken);
                batch.AddRange(recipes.Select(r => (r, host)));
            }

            return batch;
        }

        private async Task HoldingsAsync(RecipeRemovalSourceModel model, List<long> ids, List<long> curated, CancellationToken cancellationToken)
        {
            foreach (var chunk in ids.Chunk(5000))
            {
                model.InMealPlans += await _db.MealPlans.Where(m => m.RecipeId != null && chunk.Contains(m.RecipeId.Value)).Select(m => m.RecipeId).Distinct().CountAsync(cancellationToken);
                model.Favourited += await _db.RecipeRatings.Where(r => r.Rating >= 4 && chunk.Contains(r.RecipeId)).Select(r => r.RecipeId).Distinct().CountAsync(cancellationToken);
                model.InCookbooks += await _db.HouseholdCookbookRecipes.Where(c => chunk.Contains(c.RecipeId)).Select(c => c.RecipeId).Distinct().CountAsync(cancellationToken);
                model.InShoppingLists += await _db.ShoppingListItems.Where(i => i.RecipeId != null && chunk.Contains(i.RecipeId.Value)).Select(i => i.RecipeId).Distinct().CountAsync(cancellationToken);
                model.EditedByPerson += await _db.AuditLogEntries.AsNoTracking()
                    .Where(a => chunk.Contains(a.EntityId)
                        && ((a.EntityType == "RecipeStep" && a.ChangeType == RecipeOrchestrationService.EditBaseChangeType)
                            || (a.EntityType == ProseRecheckService.RecipeEntityType && a.ChangeType == ProseRecheckService.DescriptionByPersonChangeType)))
                    .Select(a => a.EntityId).Distinct().CountAsync(cancellationToken);
            }

            var references = await _db.References.AsNoTracking().Where(r => r.Name == "Recipe" || r.Name == "Approval").Select(r => new { r.Id, r.Name }).ToListAsync(cancellationToken);
            var recipeTypes = references.Where(r => r.Name == "Recipe").Select(r => r.Id).ToList();
            var approvalTypes = references.Where(r => r.Name == "Approval").Select(r => r.Id).ToList();
            foreach (var chunk in curated.Chunk(5000))
            {
                var approvals = await _db.CurationFeedbacks.AsNoTracking()
                    .Where(f => chunk.Contains(f.EntityId) && recipeTypes.Contains(f.EntityTypeId) && approvalTypes.Contains(f.FeedbackTypeId))
                    .Select(f => new { f.EntityId, f.AdminId })
                    .ToListAsync(cancellationToken);
                model.ApprovedByPerson += approvals.Where(f => f.AdminId != SystemConstants.SystemPersonId).Select(f => f.EntityId).Distinct().Count();
                model.ApprovedUnrecorded += chunk.Length - approvals.Select(f => f.EntityId).Distinct().Count();
            }
        }

        /// <summary>
        /// Drops the recipe's stored diet-gate rows at once: on Postgres by a direct delete in the batch's transaction, because the
        /// gate's own triggers clear the same rows when the recipe, its lines or its steps are written, and a tracked delete of a row
        /// a trigger already removed would fail the save.
        /// </summary>
        private async Task<(int Flags, int States)> DropDietRowsAsync(long recipeId, CancellationToken cancellationToken)
        {
            if (_db.Database.IsRelational())
                return (await _db.DietRecipeFlags.Where(f => f.RecipeId == recipeId).ExecuteDeleteAsync(cancellationToken),
                    await _db.DietRecipeStates.Where(s => s.RecipeId == recipeId).ExecuteDeleteAsync(cancellationToken));
            var flags = await _db.DietRecipeFlags.Where(f => f.RecipeId == recipeId).ToListAsync(cancellationToken);
            var states = await _db.DietRecipeStates.Where(s => s.RecipeId == recipeId).ToListAsync(cancellationToken);
            _db.DietRecipeFlags.RemoveRange(flags);
            _db.DietRecipeStates.RemoveRange(states);
            return (flags.Count, states.Count);
        }

        /// <summary>
        /// A shopping item's name once its recipe is removed: its catalog food's name, else the food words of the source's line or
        /// of the food's name (<see cref="ProseWithholding.FoodWords"/>, then the looser <see cref="ProseWithholding.PlainFoodWords"/>),
        /// and the bare <see cref="ProseWithholding.UnnamedFood"/> only when neither has any (staging checkpoint 7, S18).
        /// </summary>
        internal static string ItemName(IngredientEntity? food, string? rawLine) =>
            ProseWithholding.CatalogFood(food)
                ? food!.Name
                : ProseWithholding.FoodName(SourceRightsRule.FactsOnly, rawLine, food?.Name, false)
                  ?? ProseWithholding.PlainFoodWords(rawLine) ?? ProseWithholding.PlainFoodWords(food?.Name) ?? ProseWithholding.UnnamedFood;

        /// <summary>
        /// The foods the retire of <paramref name="recipeIds"/> leaves without a recipe: foods named from the source's lines (no FDC
        /// id, not curated, live) that a live line of these recipes names, that no live line of another recipe names, and that
        /// nothing a household or another food relies on holds (<see cref="HeldAsync"/>).
        /// </summary>
        private async Task<List<long>> OrphanFoodsAsync(List<long> recipeIds, CancellationToken cancellationToken)
        {
            var named = new HashSet<long>();
            foreach (var chunk in recipeIds.Chunk(5000))
                named.UnionWith(await _db.RecipeIngredients.IgnoreQueryFilters().Where(l => chunk.Contains(l.RecipeId) && !l.IsDeleted).Select(l => l.IngredientId).Distinct().ToListAsync(cancellationToken));
            var foods = new List<long>();
            foreach (var chunk in named.Chunk(1000))
                foods.AddRange(await _db.Ingredients.IgnoreQueryFilters()
                    .Where(i => chunk.Contains(i.Id) && !i.IsDeleted && (i.FdcId == null || i.FdcId == "") && i.CurationStatusId != (long)CurationStatusEnum.Curated)
                    .Select(i => i.Id).ToListAsync(cancellationToken));
            var used = new HashSet<long>();
            foreach (var chunk in foods.Chunk(1000))
                used.UnionWith(await _db.RecipeIngredients.IgnoreQueryFilters()
                    .Where(l => chunk.Contains(l.IngredientId) && !l.IsDeleted && !recipeIds.Contains(l.RecipeId))
                    .Select(l => l.IngredientId).Distinct().ToListAsync(cancellationToken));
            var left = foods.Where(id => !used.Contains(id)).ToList();
            var held = await HeldAsync(left, cancellationToken);
            return left.Where(id => !held.Contains(id)).OrderBy(id => id).ToList();
        }

        /// <summary>
        /// The foods among <paramref name="foodIds"/> that something besides a recipe line relies on: a pantry, shopping list,
        /// household shopping item, meal plan, household food, goal, restriction, substitution, variation, component, message
        /// thread or a live verified wording (the same uses <see cref="LineRelinkService.IsUsedOutsideRecipeLinesAsync"/> reads).
        /// </summary>
        private async Task<HashSet<long>> HeldAsync(List<long> foodIds, CancellationToken cancellationToken)
        {
            var held = new HashSet<long>();
            foreach (var part in foodIds.Chunk(1000))
            {
                var c = part.ToList();
                held.UnionWith(await _db.ShoppingListItems.Where(x => x.IngredientId != null && c.Contains(x.IngredientId.Value)).Select(x => x.IngredientId!.Value).ToListAsync(cancellationToken));
                held.UnionWith(await _db.HouseholdShoppingItems.Where(x => x.IngredientId != null && c.Contains(x.IngredientId.Value)).Select(x => x.IngredientId!.Value).ToListAsync(cancellationToken));
                held.UnionWith(await _db.PantryItems.Where(x => c.Contains(x.IngredientId)).Select(x => x.IngredientId).ToListAsync(cancellationToken));
                held.UnionWith(await _db.MealPlans.Where(x => x.IngredientId != null && c.Contains(x.IngredientId.Value)).Select(x => x.IngredientId!.Value).ToListAsync(cancellationToken));
                held.UnionWith(await _db.HouseholdIngredients.Where(x => c.Contains(x.IngredientId)).Select(x => x.IngredientId).ToListAsync(cancellationToken));
                held.UnionWith(await _db.GoalItems.Where(x => x.IngredientId != null && c.Contains(x.IngredientId.Value)).Select(x => x.IngredientId!.Value).ToListAsync(cancellationToken));
                held.UnionWith(await _db.Restrictions.Where(x => x.IngredientId != null && c.Contains(x.IngredientId.Value)).Select(x => x.IngredientId!.Value).ToListAsync(cancellationToken));
                held.UnionWith(await _db.Set<Nom.Data.Plan.RestrictionCriterionEntity>().Where(x => x.IngredientId != null && c.Contains(x.IngredientId.Value)).Select(x => x.IngredientId!.Value).ToListAsync(cancellationToken));
                held.UnionWith(await _db.MessageThreads.Where(x => x.IngredientId != null && c.Contains(x.IngredientId.Value)).Select(x => x.IngredientId!.Value).ToListAsync(cancellationToken));
                held.UnionWith(await _db.IngredientWordings.Where(x => x.IngredientId != null && c.Contains(x.IngredientId.Value)).Select(x => x.IngredientId!.Value).ToListAsync(cancellationToken));
                held.UnionWith(await _db.RecipeAugmentations.Where(x => c.Contains(x.IngredientId)).Select(x => x.IngredientId).ToListAsync(cancellationToken));
                held.UnionWith(await _db.RecipeSubstitutions.Where(x => c.Contains(x.IngredientId)).Select(x => x.IngredientId).ToListAsync(cancellationToken));
                held.UnionWith(await _db.RecipeSubstitutions.Where(x => c.Contains(x.SubstituteIngredientId)).Select(x => x.SubstituteIngredientId).ToListAsync(cancellationToken));
                held.UnionWith(await _db.Set<RecipeVariationItemEntity>().Where(x => c.Contains(x.IngredientId)).Select(x => x.IngredientId).ToListAsync(cancellationToken));
                held.UnionWith(await _db.Set<RecipeVariationItemEntity>().Where(x => c.Contains(x.SubstituteIngredientId)).Select(x => x.SubstituteIngredientId).ToListAsync(cancellationToken));
                held.UnionWith(await _db.Set<IngredientSubstitutionEntity>().Where(x => c.Contains(x.IngredientId)).Select(x => x.IngredientId).ToListAsync(cancellationToken));
                held.UnionWith(await _db.Set<IngredientSubstitutionEntity>().Where(x => c.Contains(x.SubstituteIngredientId)).Select(x => x.SubstituteIngredientId).ToListAsync(cancellationToken));
                held.UnionWith(await _db.Set<IngredientComponentEntity>().Where(x => c.Contains(x.IngredientId)).Select(x => x.IngredientId).ToListAsync(cancellationToken));
                held.UnionWith(await _db.Set<IngredientComponentEntity>().Where(x => c.Contains(x.ComponentIngredientId)).Select(x => x.ComponentIngredientId).ToListAsync(cancellationToken));
            }

            held.IntersectWith(foodIds);
            return held;
        }

        /// <summary>Retires the foods <see cref="OrphanFoodsAsync"/> found, under the run's stamp, one journal row each, so the undo by that stamp puts them back.</summary>
        private async Task RetireFoodsAsync(List<long> foodIds, RecipeRemovalRunEntity run, long personId, CancellationToken cancellationToken)
        {
            foreach (var chunk in foodIds.Chunk(1000))
            {
                foreach (var food in await _db.Ingredients.IgnoreQueryFilters().Where(i => chunk.Contains(i.Id) && !i.IsDeleted).ToListAsync(cancellationToken))
                {
                    SoftDelete(food, run.Stamp, personId);
                    _db.AuditLogEntries.Add(Entry(FoodEntityType, food.Id, FoodRetireChangeType, nameof(BaseEntity.IsDeleted), "false",
                        $"true | retired with the recipes of retire run {run.Id.ToString(CultureInfo.InvariantCulture)}: no other recipe uses it", run.Stamp, personId));
                }
            }
        }

        /// <summary>
        /// The foods a line of <paramref name="recipeIds"/> retired at <paramref name="stamp"/> names that a retire retired and still
        /// holds retired, under that stamp or a later run's (a food another recipe shared goes with the run that retired its last
        /// live line, so an earlier run's undo must bring it back with its lines).
        /// </summary>
        private async Task<List<long>> RetiredFoodsAsync(List<long> recipeIds, DateTime stamp, CancellationToken cancellationToken)
        {
            var named = new HashSet<long>();
            foreach (var chunk in recipeIds.Chunk(5000))
                named.UnionWith(await _db.RecipeIngredients.IgnoreQueryFilters().Where(l => chunk.Contains(l.RecipeId) && l.IsDeleted && l.DeletedAt == stamp).Select(l => l.IngredientId).Distinct().ToListAsync(cancellationToken));
            var foods = new List<long>();
            foreach (var chunk in named.Chunk(1000))
                foods.AddRange(await RetiredFoodsOf(_db, chunk).ToListAsync(cancellationToken));
            return foods.OrderBy(id => id).ToList();
        }

        /// <summary>The foods among <paramref name="foodIds"/> a retire retired and journaled, still retired under that retire's stamp.</summary>
        internal static IQueryable<long> RetiredFoodsOf(ApplicationDbContext db, IReadOnlyCollection<long> foodIds) =>
            db.Ingredients.IgnoreQueryFilters()
                .Where(i => foodIds.Contains(i.Id) && i.IsDeleted && i.DeletedAt != null
                    && db.AuditLogEntries.Any(a => a.EntityType == FoodEntityType && a.ChangeType == FoodRetireChangeType && a.EntityId == i.Id
                        && a.OldValue == "false" && a.Timestamp == i.DeletedAt))
                .Select(i => i.Id);

        /// <summary>The texts among <paramref name="texts"/> (trimmed, lower case) that a line of a recipe outside <paramref name="recipeIds"/> holds.</summary>
        internal static IQueryable<string> TextsInUseOf(ApplicationDbContext db, IReadOnlyCollection<long> recipeIds, IReadOnlyCollection<string> texts) =>
            db.RecipeIngredients.IgnoreQueryFilters()
                .Where(l => !recipeIds.Contains(l.RecipeId) && texts.Contains(l.RawLine.ToLower().Trim()))
                .Select(l => l.RawLine.ToLower().Trim())
                .Distinct();

        /// <summary>The link proposals whose current value is one of <paramref name="texts"/> (trimmed, lower case).</summary>
        internal static IQueryable<long> ProposalsWordedOf(ApplicationDbContext db, IReadOnlyCollection<string> texts) =>
            db.FoodCatalogProposals.IgnoreQueryFilters().Where(p => p.CurrentValue != null && texts.Contains(p.CurrentValue.ToLower().Trim())).Select(p => p.Id);

        /// <summary>What a purge of <paramref name="recipeIds"/> clears beyond the recipes' own rows (staging checkpoint 7, S20).</summary>
        private sealed record WordingPlan(HashSet<long> Foods, HashSet<long> Held, HashSet<long> Proposals, HashSet<string> Texts, HashSet<string> Wordings, List<string> Subjects, List<string> RetiredContexts);

        /// <summary>
        /// The source's line wordings of <paramref name="recipeIds"/> that the purge follows out of the recipes' own rows: the foods
        /// named from those lines (no FDC id, not curated: a line's food, or a retired food merged away from a line, named by such a
        /// text below, under that name or its tombstoned "X (deleted #id)" name) that no line of any other recipe names, live or
        /// retired; the line texts no line of any other recipe holds and no other live food is named by; the link proposals on
        /// those foods or whose current value is such a text; and the wording lanes' decisions about such a text's key when the key
        /// is free too and no live verified wording has it. Read before the recipes' lines are deleted, so a food two purged
        /// batches share goes with the later one.
        /// </summary>
        private async Task<WordingPlan> WordingPlanAsync(List<long> recipeIds, CancellationToken cancellationToken)
        {
            var lines = new List<(long Food, string Raw)>();
            foreach (var chunk in recipeIds.Chunk(5000))
                lines.AddRange((await _db.RecipeIngredients.IgnoreQueryFilters().Where(l => chunk.Contains(l.RecipeId)).Select(l => new { l.IngredientId, l.RawLine }).ToListAsync(cancellationToken))
                    .Select(l => (l.IngredientId, l.RawLine ?? string.Empty)));
            var lineFoods = new List<(long Id, string Name)>();
            foreach (var chunk in lines.Select(l => l.Food).Distinct().Chunk(1000))
                lineFoods.AddRange((await _db.Ingredients.IgnoreQueryFilters().AsNoTracking()
                        .Where(i => chunk.Contains(i.Id) && (i.FdcId == null || i.FdcId == "") && i.CurationStatusId != (long)CurationStatusEnum.Curated)
                        .Select(i => new { i.Id, i.Name }).ToListAsync(cancellationToken))
                    .Select(i => (i.Id, i.Name)));

            var texts = new HashSet<string>(StringComparer.Ordinal);
            var wordings = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (_, raw) in lines)
            {
                var plain = Text(raw);
                var normalized = Text(IngredientNameNormalizer.Normalize(raw));
                if (plain is not null) texts.Add(plain);
                if (normalized is not null) texts.Add(normalized);
                if (plain is not null && (plain != Text(ProseWithholding.FoodWords(raw)) || IngredientNameNormalizer.TierKey(raw) is null)) wordings.Add(plain);
            }
            foreach (var (_, name) in lineFoods)
                if (Text(name) is { } named) texts.Add(named);

            var foods = await NotUsedElsewhereAsync(recipeIds, lineFoods.Select(f => f.Id).ToHashSet(), cancellationToken);
            var foodList = foods.ToList();
            var free = await FreeOfAsync(recipeIds, texts, foodList, cancellationToken);
            var freeList = free.ToList();

            var mergedAway = new HashSet<long>();
            foreach (var chunk in freeList.Chunk(1000))
                mergedAway.UnionWith(await MergedAwayFoodsOf(_db, chunk).ToListAsync(cancellationToken));
            mergedAway.ExceptWith(foods);
            foods.UnionWith(await NotUsedElsewhereAsync(recipeIds, mergedAway, cancellationToken));
            foodList = foods.ToList();

            var proposals = new HashSet<long>();
            foreach (var chunk in foodList.Chunk(1000))
                proposals.UnionWith(await _db.FoodCatalogProposals.IgnoreQueryFilters().Where(p => p.IngredientId != null && chunk.Contains(p.IngredientId.Value)).Select(p => p.Id).ToListAsync(cancellationToken));
            foreach (var chunk in freeList.Chunk(1000))
                proposals.UnionWith(await ProposalsWordedOf(_db, chunk).ToListAsync(cancellationToken));

            var keys = free.Select(IngredientNameNormalizer.WordingKey).Select(Text).OfType<string>().ToHashSet(StringComparer.Ordinal);
            var freeKeys = await FreeOfAsync(recipeIds, keys, foodList, cancellationToken);
            foreach (var chunk in freeKeys.ToList().Chunk(1000))
                freeKeys.ExceptWith(await _db.IngredientWordings.Where(w => chunk.Contains(w.Wording)).Select(w => w.Wording).ToListAsync(cancellationToken));
            var subjects = freeKeys.Select(WordingTierLaneService.Subject).OrderBy(s => s, StringComparer.Ordinal).ToList();
            wordings.IntersectWith(free);
            return new WordingPlan(foods, await HeldAsync(foodList, cancellationToken), proposals, free, wordings, subjects, foodList.Select(RetiredAliasContext).ToList());

            static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
        }

        /// <summary>The foods among <paramref name="candidates"/> that no line of a recipe outside <paramref name="recipeIds"/> names, live or retired.</summary>
        private async Task<HashSet<long>> NotUsedElsewhereAsync(List<long> recipeIds, HashSet<long> candidates, CancellationToken cancellationToken)
        {
            var usedElsewhere = new HashSet<long>();
            foreach (var chunk in candidates.Chunk(1000))
                usedElsewhere.UnionWith(await _db.RecipeIngredients.IgnoreQueryFilters()
                    .Where(l => chunk.Contains(l.IngredientId) && !recipeIds.Contains(l.RecipeId)).Select(l => l.IngredientId).Distinct().ToListAsync(cancellationToken));
            return candidates.Where(id => !usedElsewhere.Contains(id)).ToHashSet();
        }

        /// <summary>The texts among <paramref name="texts"/> that no line of a recipe outside <paramref name="recipeIds"/> holds and no live food outside <paramref name="foods"/> is named by.</summary>
        private async Task<HashSet<string>> FreeOfAsync(List<long> recipeIds, HashSet<string> texts, List<long> foods, CancellationToken cancellationToken)
        {
            var inUse = new HashSet<string>(StringComparer.Ordinal);
            foreach (var chunk in texts.ToList().Chunk(1000))
            {
                inUse.UnionWith(await TextsInUseOf(_db, recipeIds, chunk).ToListAsync(cancellationToken));
                inUse.UnionWith(await _db.Ingredients
                    .Where(i => !foods.Contains(i.Id) && chunk.Contains(i.Name.ToLower().Trim())).Select(i => i.Name.ToLower().Trim()).Distinct().ToListAsync(cancellationToken));
            }
            return texts.Where(t => !inUse.Contains(t)).ToHashSet(StringComparer.Ordinal);
        }

        /// <summary>
        /// The retired foods (no FDC id, not curated) named by one of <paramref name="texts"/> (trimmed, lower case), under that
        /// name or under the "X (deleted #id)" name a merge or a freed name gives a retired food.
        /// </summary>
        internal static IQueryable<long> MergedAwayFoodsOf(ApplicationDbContext db, IReadOnlyCollection<string> texts) =>
            db.Ingredients.IgnoreQueryFilters()
                .Where(i => i.IsDeleted && (i.FdcId == null || i.FdcId == "") && i.CurationStatusId != (long)CurationStatusEnum.Curated
                    && (texts.Contains(i.Name.ToLower().Trim())
                        || (i.Name.Contains(TombstoneMark) && texts.Contains(i.Name.Substring(0, i.Name.IndexOf(TombstoneMark)).ToLower().Trim()))))
                .Select(i => i.Id);

        /// <summary>The aliases a relink gave the food that took a retired food's lines, under the retired food's name.</summary>
        internal static IQueryable<IngredientAliasEntity> RetiredAliasesOf(ApplicationDbContext db, IReadOnlyCollection<string> contexts) =>
            db.IngredientAliases.IgnoreQueryFilters().Where(a => a.SourceContext != null && contexts.Contains(a.SourceContext));

        /// <summary>The aliases, on any food, whose name (trimmed, lower case) is one of <paramref name="texts"/>.</summary>
        internal static IQueryable<IngredientAliasEntity> AliasesWordedOf(ApplicationDbContext db, IReadOnlyCollection<string> texts) =>
            db.IngredientAliases.IgnoreQueryFilters().Where(a => texts.Contains(a.AliasName.ToLower().Trim()));

        /// <summary>
        /// The aliases the purge's other alias rules leave whose name is a free line wording of the purged lines, a whole line
        /// that is more than its food words (staging checkpoint 8, T17: a link lane gave a catalog food the line wording "For the
        /// glaze" as an alias, with no retired-food context). A line's food words, or a line that is only food words and no
        /// heading, name no alias here: a catalog food's alias "unsalted butter" serves every other recipe.
        /// </summary>
        private async Task<List<IngredientAliasEntity>> WordedAliasesAsync(WordingPlan plan, CancellationToken cancellationToken)
        {
            var aliases = new List<IngredientAliasEntity>();
            foreach (var chunk in plan.Wordings.ToList().Chunk(1000))
                aliases.AddRange(await AliasesWordedOf(_db, chunk)
                    .Where(a => !plan.Foods.Contains(a.IngredientId) && (a.SourceContext == null || !plan.RetiredContexts.Contains(a.SourceContext)))
                    .ToListAsync(cancellationToken));
            return aliases;
        }

        private static string RetiredAliasContext(long foodId) => LineRelinkService.RetiredContextPrefix + foodId.ToString(CultureInfo.InvariantCulture);

        private IQueryable<AuditLogEntryEntity> FoodAuditRows(WordingPlan plan)
        {
            var foods = plan.Foods.ToList();
            var proposals = plan.Proposals.ToList();
            return _db.AuditLogEntries.Where(a => !OwnChangeTypes.Contains(a.ChangeType)
                && ((a.EntityType == FoodEntityType && foods.Contains(a.EntityId))
                    || (a.EntityType == LineRelinkService.PickWithdrawEntityType && proposals.Contains(a.EntityId))));
        }

        /// <summary>
        /// Clears what <see cref="WordingPlanAsync"/> found: a food a household or another food holds is renamed by its food words
        /// (made unique by its id when another food has that name); any other is given a purged placeholder name and retired; their
        /// aliases, deleted wording rows and wording rejections are deleted, and so is any alias of another food named by a free line wording; the proposals' text and the journal rows of those
        /// foods and proposals are replaced by a hash; the wording lanes' decisions about the texts are cleared and their subject
        /// replaced by a hash.
        /// </summary>
        private async Task PurgeWordingsAsync(WordingPlan plan, DateTime stamp, long personId, CancellationToken cancellationToken)
        {
            var foodList = plan.Foods.ToList();
            foreach (var chunk in foodList.Chunk(1000))
            {
                foreach (var food in await _db.Ingredients.IgnoreQueryFilters().Where(i => chunk.Contains(i.Id)).ToListAsync(cancellationToken))
                {
                    if (food.Name.StartsWith(PurgedValuePrefix, StringComparison.Ordinal)) continue;
                    if (plan.Held.Contains(food.Id))
                    {
                        var plain = ProseWithholding.PlainFoodWords(food.Name) ?? ProseWithholding.UnnamedFood;
                        var taken = await _db.Ingredients.IgnoreQueryFilters().AnyAsync(i => i.Id != food.Id && i.Name == plain, cancellationToken);
                        food.Name = taken ? $"{plain} ({food.Id.ToString(CultureInfo.InvariantCulture)})" : plain;
                        food.NameNormalized = food.Name.ToLowerInvariant();
                    }
                    else
                    {
                        food.Name = PurgedValue(food.Name);
                        food.NameNormalized = null;
                        if (!food.IsDeleted) SoftDelete(food, stamp, personId);
                    }

                    food.PluralName = null;
                    food.PluralNameNormalized = null;
                    food.Description = null;
                }

                _db.IngredientAliases.RemoveRange(await _db.IngredientAliases.IgnoreQueryFilters().Where(a => chunk.Contains(a.IngredientId)).ToListAsync(cancellationToken));
                _db.IngredientWordings.RemoveRange(await _db.IngredientWordings.IgnoreQueryFilters().Where(w => w.IsDeleted && w.IngredientId != null && chunk.Contains(w.IngredientId.Value)).ToListAsync(cancellationToken));
                _db.IngredientWordingRejections.RemoveRange(await _db.IngredientWordingRejections.IgnoreQueryFilters().Where(w => w.IngredientId != null && chunk.Contains(w.IngredientId.Value)).ToListAsync(cancellationToken));
            }

            foreach (var chunk in plan.RetiredContexts.Chunk(1000))
                _db.IngredientAliases.RemoveRange(await RetiredAliasesOf(_db, chunk).Where(a => !foodList.Contains(a.IngredientId)).ToListAsync(cancellationToken));
            _db.IngredientAliases.RemoveRange(await WordedAliasesAsync(plan, cancellationToken));

            var proposalList = plan.Proposals.ToList();
            foreach (var chunk in proposalList.Chunk(1000))
            {
                foreach (var proposal in await _db.FoodCatalogProposals.IgnoreQueryFilters().Where(p => chunk.Contains(p.Id)).ToListAsync(cancellationToken))
                {
                    proposal.CurrentValue = Purged(proposal.CurrentValue);
                    if (proposal.ProposedValue != null && !long.TryParse(proposal.ProposedValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
                        && !decimal.TryParse(proposal.ProposedValue, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
                        proposal.ProposedValue = Purged(proposal.ProposedValue);
                    if (proposal.Reason != null && plan.Texts.Any(t => proposal.Reason.Contains(t, StringComparison.OrdinalIgnoreCase)))
                        proposal.Reason = Purged(proposal.Reason);
                }
            }

            foreach (var row in await FoodAuditRows(plan).ToListAsync(cancellationToken))
            {
                row.OldValue = Purged(row.OldValue);
                row.NewValue = Purged(row.NewValue);
            }

            foreach (var chunk in plan.Subjects.Chunk(1000))
            {
                foreach (var decision in await _db.LaneDecisions.IgnoreQueryFilters().Where(d => chunk.Contains(d.Subject)).ToListAsync(cancellationToken))
                {
                    decision.Subject = WordingTierLaneService.Subject(PurgedValue(decision.Subject));
                    decision.SignalsJson = null;
                    decision.VerdictsJson = null;
                    decision.AuditJson = null;
                }
            }

            static string? Purged(string? value) => value == null || value.StartsWith(PurgedValuePrefix, StringComparison.Ordinal) ? value : PurgedValue(value);
        }

        /// <summary>One source's share of a purge, with what households and people hold of its retired recipes, counted as the retire counts them (staging checkpoint 7, S19).</summary>
        private async Task<RecipeRemovalSourceModel> PurgeSourceAsync(List<RecipeRemovalEntity> removals, CancellationToken cancellationToken)
        {
            var first = removals[0];
            var model = new RecipeRemovalSourceModel { SourceId = first.ScrapingSourceId, Domain = first.Host, RightsClass = first.RightsClass, Recipes = removals.Count };
            var ids = removals.Select(r => r.RecipeId).ToList();
            model.AlreadyDeleted = removals.Count(r => r.WasDeleted);
            var curated = new List<long>();
            foreach (var chunk in ids.Chunk(5000))
                curated.AddRange(await _db.Recipes.IgnoreQueryFilters().Where(r => chunk.Contains(r.Id) && r.CurationStatusId == (long)CurationStatusEnum.Curated).Select(r => r.Id).ToListAsync(cancellationToken));
            model.Approved = curated.Count;
            await HoldingsAsync(model, ids, curated, cancellationToken);
            return model;
        }

        private static RecipeRemovalSourceModel Model(HostSource host) => new()
        {
            SourceId = host.SourceId, Domain = host.Domain, RightsClass = host.RightsClass, RightsQuote = host.RightsQuote, RightsQuoteUrl = host.RightsQuoteUrl,
        };

        private async Task InBatchAsync(Func<Task> work, CancellationToken cancellationToken)
        {
            if (!_db.Database.IsRelational())
            {
                await work();
                return;
            }

            await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
            await work();
            await transaction.CommitAsync(cancellationToken);
        }

        private static void SoftDelete(BaseEntity row, DateTime stamp, long personId)
        {
            row.IsDeleted = true;
            row.DeletedAt = stamp;
            row.DeletedByPersonId = personId;
        }

        private static void Restore(BaseEntity row)
        {
            row.IsDeleted = false;
            row.DeletedAt = null;
            row.DeletedByPersonId = null;
        }

        /// <summary>A stamp at whole milliseconds, so the stamp a soft delete carries reads back equal from the database.</summary>
        private static DateTime Stamped(DateTime at) => new(at.Ticks - at.Ticks % TimeSpan.TicksPerMillisecond, DateTimeKind.Utc);

        private static string PurgedValue(string value) => $"{PurgedValuePrefix}{SourceRecord.Sha256(value)})";

        private static void Skip(RecipeRemovalReportModel report, string reason)
        {
            report.Skipped++;
            report.SkippedByReason[reason] = report.SkippedByReason.GetValueOrDefault(reason) + 1;
        }

        private static AuditLogEntryEntity Entry(string entityType, long entityId, string changeType, string property, string? oldValue, string? newValue, DateTime at, long by) => new()
        {
            EntityType = entityType,
            EntityId = entityId,
            ChangeType = changeType,
            PropertyName = property,
            OldValue = Clip(oldValue, 4000),
            NewValue = Clip(newValue, 4000),
            Timestamp = at,
            ChangedByPersonId = by,
        };

        private static string? Clip(string? value, int length) =>
            value == null || value.Length <= length ? value : value[..length];
    }
}

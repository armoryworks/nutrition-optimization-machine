using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nom.Data;
using Nom.Data.Audit;
using Nom.Data.Curation;
using Nom.Data.Nutrient;
using Nom.Data.Nutrition;
using Nom.Data.Recipe;
using Nom.Data.Reference;
using Nom.Orch.Services;

namespace Nom.Import.Services
{
    /// <summary>
    /// Focused loader for USDA FDC Foundation Foods (CC0). Reads the bulk CSVs, applies the
    /// quality gate (per-100g plausibility + energy by USDA's own calorie factors), classifies each
    /// food by its FDC food category only, and lands accepted foods with per-100g nutrients as
    /// PendingCuration with the verdict of <see cref="CatalogAdmissionRule"/> stored; only the gated
    /// `--admit` pass (<see cref="CatalogAdmissionService"/>) curates them. Idempotent by FdcId.
    /// Reports accepted/rejected so quality can be gauged in staging before touching prod.
    /// Run: `dotnet run -- --import-fdc &lt;csv-dir&gt; [--sr-legacy] [--only &lt;ids-file&gt;] [--dry-run]`.
    /// Everything one run writes shares one timestamp and is journaled in the audit log
    /// (change type <see cref="ImportChangeType"/>), which <see cref="UndoImportAsync"/> reverses:
    /// `--import-fdc --undo &lt;stamp&gt; [--dry-run]`.
    /// <see cref="AttachAsync"/> gives a named catalog food a named USDA food in place:
    /// `--attach-fdc &lt;csv-dir&gt; &lt;pairs.tsv&gt; [--sr-legacy] [--dry-run]`, reversed by
    /// `--attach-fdc --undo &lt;pairs.tsv&gt; [--dry-run]`.
    /// </summary>
    public class FdcFoundationImportService
    {
        public const string FoundationFood = "foundation_food";
        public const string AttachField = "fdc_attach";
        public const string SrLegacyFood = "sr_legacy_food";
        public const string AttachBatch = "fdc-attach";
        public const string ImportChangeType = "FdcImport";
        public const string ImportUndoChangeType = "FdcImportUndo";
        public const string ImportEntityType = "Ingredient";

        private readonly ApplicationDbContext _db;
        private readonly ILogger<FdcFoundationImportService> _logger;
        private readonly FoodDataQualityValidator _validator = new();

        public FdcFoundationImportService(ApplicationDbContext db, ILogger<FdcFoundationImportService> logger)
        {
            _db = db;
            _logger = logger;
        }

        /// <summary>
        /// Every accepted food is judged by <see cref="CatalogAdmissionRule"/> as it is created and
        /// lands PendingCuration, with its food group and edibility from its USDA category only and
        /// its category, energy check and the rule's reasons (none when the rule would curate it)
        /// stored. The `--admit` pass curates it, after its file, energy and size gates.
        /// </summary>
        /// <param name="dataType">
        /// Which FDC data type to read from food.csv: <see cref="FoundationFood"/> (default) or
        /// <see cref="SrLegacyFood"/> (USDA Standard Reference — the generic staples recipes name).
        /// </param>
        /// <param name="onlyFdcIds">When given, only the foods with these FDC ids are read from the file.</param>
        /// <param name="dryRun">Report what would be written without saving anything.</param>
        /// <param name="createFoods">
        /// False backfills the foods already in the catalog and creates none: a whole SR Legacy file
        /// holds foods whose catalog rows were moved to Foundation records, and creating them again
        /// would add pending duplicates of curated foods.
        /// </param>
        public async Task<ImportReport> ImportAsync(string csvDir, CancellationToken ct = default, string dataType = FoundationFood,
            IReadOnlySet<string>? onlyFdcIds = null, bool dryRun = false, bool createFoods = true)
        {
            var foodCsv = FindFile(csvDir, "food.csv");
            var nutrientCsv = FindFile(csvDir, "food_nutrient.csv");

            _logger.LogInformation("Reading foundation foods from {Dir}", csvDir);
            var foods = ReadFoods(foodCsv, dataType);
            var listedNotInFile = new List<string>();
            if (onlyFdcIds != null)
            {
                listedNotInFile.AddRange(onlyFdcIds.Where(id => !foods.ContainsKey(id)).OrderBy(id => id, StringComparer.Ordinal));
                foods = foods.Where(f => onlyFdcIds.Contains(f.Key)).ToDictionary(f => f.Key, f => f.Value);
            }
            var nutrients = FdcNutrients.Read(nutrientCsv, foods.Keys.ToHashSet());
            var categories = UsdaFoodFacts.Categories(TryFindFile(csvDir, "food_category.csv"));
            var factors = ReadCalorieFactors(csvDir, foods.Keys.ToHashSet());
            var curatedKeys = await CatalogAdmissionService.CuratedKeysAsync(_db, ct);

            var portionRows = UsdaPortionFile.Read(TryFindFile(csvDir, "food_portion.csv"), foods.Keys.ToHashSet());
            var portions = ReferenceServings(portionRows);
            var densities = Densities(portionRows);
            var itemWeights = ItemWeights(portionRows, foods);
            var targets = await FdcNutrients.ResolveAsync(_db, _logger, ct);

            var linked = await _db.Ingredients.IgnoreQueryFilters()
                .Where(i => i.FdcId != null).Select(i => new { i.Id, i.FdcId, i.IsDeleted }).ToListAsync(ct);
            var undoneIds = linked.Where(i => i.IsDeleted).Select(i => i.Id).ToList();
            var retiredByUndo = (await _db.AuditLogEntries
                    .Where(a => a.EntityType == ImportEntityType && a.ChangeType == ImportUndoChangeType && undoneIds.Contains(a.EntityId))
                    .Select(a => new { a.Id, a.EntityId, a.NewValue })
                    .ToListAsync(ct))
                .Where(a => linked.Any(i => i.Id == a.EntityId && i.FdcId == a.NewValue))
                .GroupBy(a => a.NewValue!)
                .Where(g => g.Select(a => a.EntityId).Distinct().Count() == 1 && !linked.Any(i => i.FdcId == g.Key && !i.IsDeleted))
                .ToDictionary(g => g.Key, g => (FoodId: g.First().EntityId, EntryIds: g.Select(a => a.Id).ToList()));
            var revive = new List<(long FoodId, List<long> EntryIds)>();
            var existingFdcIds = linked.Select(i => i.FdcId!).ToHashSet();
            var existingByFdc = linked.Where(i => !i.IsDeleted)
                .GroupBy(i => i.FdcId!).ToDictionary(g => g.Key, g => g.First().Id);
            // Ingredient.Name is unique — dedupe against existing names and within this run.
            var catalogByName = (await _db.Ingredients
                .Where(i => !i.IsDeleted)
                .Select(i => new { i.Id, i.Name, i.FdcId }).ToListAsync(ct))
                .GroupBy(i => i.Name.ToLowerInvariant())
                .ToDictionary(g => g.Key, g => (g.First().Id, g.First().FdcId));
            var seenNames = catalogByName.Keys.ToHashSet();
            var claimed = (await _db.FoodCatalogProposals
                .Where(p => p.FdcId != null && p.IngredientId != null && p.Status != FoodProposalStatus.Rejected
                    && (p.Field != AttachField || p.Status == FoodProposalStatus.Pending)
                    && _db.Ingredients.Any(i => i.Id == p.IngredientId && !i.IsDeleted))
                .Select(p => p.FdcId!)
                .Distinct()
                .ToListAsync(ct)).ToHashSet();
            var attachRequested = (await _db.FoodCatalogProposals
                .Where(p => p.Field == AttachField && p.IngredientId != null)
                .Select(p => p.IngredientId!.Value).ToListAsync(ct)).ToHashSet();
            var attach = new List<(long IngredientId, string FdcId, string Name, FdcFoodNutrients Nutrients, decimal? Serving, decimal? Density, decimal? Item)>();

            var now = new DateTime(DateTime.UtcNow.Ticks / 10 * 10, DateTimeKind.Utc);
            var report = new ImportReport { TotalFoundation = foods.Count, DryRun = dryRun, RunStamp = now };
            report.ListedNotInFile.AddRange(listedNotInFile);
            var pendingNutrition = new List<(IngredientEntity Ingredient, FdcFoodNutrients Nutrients)>();

            var backfill = new List<(long IngredientId, FdcFoodNutrients Nutrients)>();
            foreach (var (fdcId, food) in foods)
            {
                ct.ThrowIfCancellationRequested();
                if (retiredByUndo.TryGetValue(fdcId, out var undone) && createFoods)
                {
                    revive.Add(undone);
                    report.Revived++;
                    continue;
                }
                if (existingFdcIds.Contains(fdcId))
                {
                    report.SkippedExisting++;
                    if (!existingByFdc.TryGetValue(fdcId, out var existingId)) report.SkippedRetired++;
                    else if (nutrients.TryGetValue(fdcId, out var known)) backfill.Add((existingId, known));
                    continue;
                }
                if (claimed.Contains(fdcId))
                {
                    report.SkippedClaimed.Add(fdcId);
                    continue;
                }
                if (!createFoods)
                {
                    report.SkippedNotCreated++;
                    continue;
                }
                if (!nutrients.TryGetValue(fdcId, out var m)) { report.RejectedByReason["nutrients_missing"] = report.RejectedByReason.GetValueOrDefault("nutrients_missing") + 1; report.Rejected++; continue; }

                var quality = m.QualityInput(food.Description, UsedInSmallAmounts(food), factors.GetValueOrDefault(fdcId));
                var result = _validator.Validate(quality);
                if (!result.Accepted)
                {
                    report.Rejected++;
                    foreach (var reason in result.Reasons)
                        report.RejectedByReason[reason] = report.RejectedByReason.GetValueOrDefault(reason) + 1;
                    continue;
                }

                var name = Truncate(food.Description, 2000);
                if (!seenNames.Add(name.ToLowerInvariant()))
                {
                    report.SkippedDuplicateName++;
                    if (catalogByName.TryGetValue(name.ToLowerInvariant(), out var same) && same.FdcId == null && attachRequested.Add(same.Id))
                    {
                        attach.Add((same.Id, fdcId, name, m, portions.TryGetValue(fdcId, out var sg) ? sg : null, densities.TryGetValue(fdcId, out var sd) ? sd : null,
                            itemWeights.TryGetValue(fdcId, out var si) ? si : null));
                    }
                    continue;
                }

                var decision = CatalogAdmissionRule.Decide(new AdmissionFacts(dataType, food.CategoryId,
                    food.CategoryId is int named ? categories.GetValueOrDefault(named) : null, food.Description, quality,
                    curatedKeys.TryGetValue(CatalogAdmissionService.Key(name), out var holder) ? holder : null), _validator);
                var group = decision.FoodGroupId;

                var ingredient = new IngredientEntity
                {
                    Name = name,
                    FdcId = fdcId,
                    FdcDataType = dataType,
                    CurationStatusId = (long)CurationStatusEnum.PendingCuration,
                    FoodGroupId = group,
                    IsWholeFood = decision.IsWholeFood,
                    FdcCategoryId = food.CategoryId,
                    FdcCategory = food.CategoryId is int c ? categories.GetValueOrDefault(c) : null,
                    EnergyCheck = decision.EnergyCheck,
                    AdmissionReason = decision.Reason,
                    // NOTE: must be TryGetValue — GetValueOrDefault on a decimal dictionary yields
                    // 0, which would silently zero out the food's nutrition.
                    ReferenceServingGrams = portions.TryGetValue(fdcId, out var refGrams) ? refGrams : null,
                    GramsPerMilliliter = densities.TryGetValue(fdcId, out var density) ? density : null,
                    GramsPerItem = itemWeights.TryGetValue(fdcId, out var item) ? item : null,
                    CreatedDate = now,
                };

                if (!dryRun) _db.Ingredients.Add(ingredient);
                pendingNutrition.Add((ingredient, m));
                if (ingredient.ReferenceServingGrams.HasValue) report.WithReferenceServing++;
                if (ingredient.GramsPerMilliliter.HasValue) report.WithDensity++;

                report.Accepted++;
                if (group.HasValue) report.Classified++;
                if (decision.Status == (long)CurationStatusEnum.Curated) report.Admissible++;
                foreach (var reason in decision.Reasons)
                    report.HeldByReason[reason] = report.HeldByReason.GetValueOrDefault(reason) + 1;
            }

            await using var tx = !dryRun && _db.Database.IsRelational() ? await _db.Database.BeginTransactionAsync(ct) : null;

            if (!dryRun && revive.Count > 0)
            {
                var reviveIds = revive.Select(r => r.FoodId).ToList();
                var entryIds = revive.SelectMany(r => r.EntryIds).ToList();
                var liveNames = (await _db.Ingredients.Where(i => !i.IsDeleted).Select(i => i.Name).ToListAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var food in await _db.Ingredients.IgnoreQueryFilters().Where(i => reviveIds.Contains(i.Id)).ToListAsync(ct))
                {
                    if (!liveNames.Add(food.Name))
                    {
                        report.Revived--;
                        report.SkippedDuplicateName++;
                        entryIds.RemoveAll(id => revive.Single(r => r.FoodId == food.Id).EntryIds.Contains(id));
                        continue;
                    }
                    food.IsDeleted = false;
                    food.DeletedAt = null;
                    food.DeletedByPersonId = null;
                    food.LastModifiedDate = now;
                    _db.AuditLogEntries.Add(Journal(food.Id, nameof(IngredientEntity.FdcId), food.FdcId, now));
                }
                _db.AuditLogEntries.RemoveRange(await _db.AuditLogEntries.Where(a => entryIds.Contains(a.Id)).ToListAsync(ct));
            }

            // Phase 1: persist the ingredients so identity values exist.
            if (!dryRun)
            {
                await _db.SaveChangesAsync(ct);
                foreach (var (ing, _) in pendingNutrition)
                    _db.AuditLogEntries.Add(Journal(ing.Id, nameof(IngredientEntity.FdcId), ing.FdcId, now));
            }

            // Phase 2: attach per-100g nutrient facts against the now-known ingredient ids.
            // (FDC amounts are per 100 g; ReferenceServingGrams carries the standard portion when
            // known, and per-person scaling happens downstream in the portions engine.)
            foreach (var (ing, m) in pendingNutrition)
                foreach (var (key, amount) in FdcNutrients.Accepted(m, _validator, r => CountDropped(report, r)))
                    AddNutrient(ing.Id, targets, key, amount, now, dryRun, ref report.NutrientRows);

            if (backfill.Count > 0)
            {
                var ids = backfill.Select(b => b.IngredientId).ToList();
                var have = (await _db.Set<IngredientNutrientEntity>()
                    .IgnoreQueryFilters()
                    .Where(n => ids.Contains(n.IngredientId))
                    .Select(n => new { n.IngredientId, n.NutrientId })
                    .ToListAsync(ct))
                    .Select(n => (n.IngredientId, n.NutrientId)).ToHashSet();
                foreach (var (ingredientId, m) in backfill)
                {
                    var before = report.BackfilledNutrientRows;
                    foreach (var (key, amount) in FdcNutrients.Accepted(m, _validator, r => CountDropped(report, r)))
                        if (targets.TryGetValue(key, out var t) && !have.Contains((ingredientId, t.NutrientId)))
                        {
                            AddNutrient(ingredientId, targets, key, amount, now, dryRun, ref report.BackfilledNutrientRows);
                            report.BackfilledByNutrient[key] = report.BackfilledByNutrient.GetValueOrDefault(key) + 1;
                        }
                    var added = report.BackfilledNutrientRows - before;
                    if (added > 0 && !dryRun)
                        _db.AuditLogEntries.Add(Journal(ingredientId, nameof(IngredientEntity.IngredientNutrients), added.ToString(CultureInfo.InvariantCulture), now));
                }
            }
            var densityBackfill = existingByFdc
                .Where(e => densities.ContainsKey(e.Key))
                .ToDictionary(e => e.Value, e => densities[e.Key]);
            if (densityBackfill.Count > 0)
            {
                var ids = densityBackfill.Keys.ToList();
                foreach (var ing in await _db.Ingredients.Where(i => ids.Contains(i.Id) && i.GramsPerMilliliter == null).ToListAsync(ct))
                {
                    report.BackfilledDensities++;
                    if (dryRun) continue;
                    ing.GramsPerMilliliter = densityBackfill[ing.Id];
                    _db.AuditLogEntries.Add(Journal(ing.Id, nameof(IngredientEntity.GramsPerMilliliter), densityBackfill[ing.Id].ToString(CultureInfo.InvariantCulture), now));
                }
            }

            foreach (var (ingredientId, fdc, name, m, serving, density, item) in attach)
            {
                var proposal = AttachProposal(ingredientId, name, fdc, name, dataType, m, serving, density, item, targets, now,
                    n => $"Same name as USDA {dataType} food {fdc}: attach its {n} nutrient values", out _);
                if (!dryRun) _db.FoodCatalogProposals.Add(proposal);
                report.AttachProposals++;
            }
            if (!dryRun) await _db.SaveChangesAsync(ct);
            if (tx != null) await tx.CommitAsync(ct);
            _logger.LogInformation("FDC foundation import{DryRun}: {Accepted} accepted, {Rejected} rejected, {Skipped} existing.",
                dryRun ? " (dry run)" : string.Empty, report.Accepted, report.Rejected, report.SkippedExisting);
            return report;
        }

        /// <summary>
        /// Takes back what the import run stamped <paramref name="stamp"/> wrote, from its audit
        /// journal: a food it created is soft-deleted unless a recipe line already uses it, the
        /// nutrient rows it added to an existing food are removed, a density it filled is cleared
        /// when unchanged, and its attach proposals still pending are rejected. A retired food keeps
        /// its FDC id and is journaled as retired by this undo (change type
        /// <see cref="ImportUndoChangeType"/>), so a later import of that id revives the same food,
        /// with its nutrient rows, instead of skipping it.
        /// </summary>
        /// <param name="dryRun">Report what would be undone without saving anything.</param>
        public async Task<ImportUndoReport> UndoImportAsync(DateTime stamp, bool dryRun, CancellationToken ct = default)
        {
            var report = new ImportUndoReport { DryRun = dryRun, RunStamp = stamp };
            var entries = await _db.AuditLogEntries
                .Where(a => a.EntityType == ImportEntityType && a.ChangeType == ImportChangeType && a.Timestamp == stamp)
                .OrderByDescending(a => a.Id)
                .ToListAsync(ct);
            var ids = entries.Select(e => e.EntityId).Distinct().ToList();
            var foods = await _db.Ingredients.IgnoreQueryFilters().Where(i => ids.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
            var inUse = (await _db.RecipeIngredients.Where(ri => ids.Contains(ri.IngredientId))
                .Select(ri => ri.IngredientId).Distinct().ToListAsync(ct)).ToHashSet();
            var now = DateTime.UtcNow;
            var touched = new List<long>();

            await using var tx = !dryRun && _db.Database.IsRelational() ? await _db.Database.BeginTransactionAsync(ct) : null;
            foreach (var entry in entries)
            {
                ct.ThrowIfCancellationRequested();
                if (!foods.TryGetValue(entry.EntityId, out var food)) { report.Skipped++; continue; }
                if (entry.PropertyName == nameof(IngredientEntity.FdcId))
                {
                    if (food.IsDeleted || food.FdcId != entry.NewValue) { report.Skipped++; continue; }
                    if (inUse.Contains(food.Id))
                    {
                        report.KeptInUse++;
                        report.Lines.Add($"kept     #{food.Id} {food.Name}: a recipe line uses it");
                        continue;
                    }
                    report.FoodsRetired++;
                    if (dryRun) continue;
                    food.IsDeleted = true;
                    food.DeletedAt = now;
                    food.DeletedByPersonId = SystemConstants.SystemPersonId;
                    food.LastModifiedDate = now;
                    _db.AuditLogEntries.Add(new AuditLogEntryEntity
                    {
                        EntityType = ImportEntityType,
                        EntityId = food.Id,
                        ChangeType = ImportUndoChangeType,
                        PropertyName = nameof(IngredientEntity.IsDeleted),
                        OldValue = stamp.ToString("O", CultureInfo.InvariantCulture),
                        NewValue = food.FdcId,
                        Timestamp = now,
                        ChangedByPersonId = SystemConstants.SystemPersonId,
                    });
                }
                else if (entry.PropertyName == nameof(IngredientEntity.GramsPerMilliliter))
                {
                    if (!decimal.TryParse(entry.NewValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var filled) || food.GramsPerMilliliter != filled)
                    {
                        report.Skipped++;
                        continue;
                    }
                    report.DensitiesCleared++;
                    if (dryRun) continue;
                    food.GramsPerMilliliter = null;
                    food.LastModifiedDate = now;
                    touched.Add(food.Id);
                }
                else if (entry.PropertyName == nameof(IngredientEntity.IngredientNutrients))
                {
                    var rows = await _db.Set<IngredientNutrientEntity>()
                        .Where(n => n.IngredientId == food.Id && n.CreatedDate == stamp).ToListAsync(ct);
                    report.NutrientRowsRemoved += rows.Count;
                    if (dryRun) continue;
                    _db.Set<IngredientNutrientEntity>().RemoveRange(rows);
                    touched.Add(food.Id);
                }
                else
                {
                    report.Skipped++;
                    continue;
                }
                _db.AuditLogEntries.Remove(entry);
            }

            var proposals = await _db.FoodCatalogProposals
                .Where(p => p.Field == AttachField && p.Batch == AttachBatch && p.Status == FoodProposalStatus.Pending && p.CreatedDate == stamp)
                .ToListAsync(ct);
            report.ProposalsRejected = proposals.Count;
            if (dryRun) return report;

            Reject(proposals, now);
            await _db.SaveChangesAsync(ct);
            await TouchLinesAsync(touched, now, ct);
            if (tx != null) await tx.CommitAsync(ct);
            _logger.LogInformation("FDC import undo: {Foods} foods retired, {Rows} nutrient rows removed.", report.FoodsRetired, report.NutrientRowsRemoved);
            return report;
        }

        /// <summary>One catalog food to give a USDA food to: its id, its name (which must match) and the FDC id.</summary>
        public sealed record AttachRequest(long IngredientId, string Name, string FdcId);

        /// <summary>
        /// Reads the tab-separated pairs file of <see cref="AttachAsync"/>: a header row naming the
        /// columns <c>ingredient_id</c>, <c>name</c> and <c>fdc_id</c>, then one food per row.
        /// </summary>
        public static List<AttachRequest> ReadAttachRequests(string path)
        {
            var lines = File.ReadAllLines(path).Where(l => l.Trim().Length > 0).ToList();
            var requests = new List<AttachRequest>();
            if (lines.Count < 2) return requests;

            var header = lines[0].Split('\t').Select(h => h.Trim().ToLowerInvariant()).ToArray();
            foreach (var line in lines.Skip(1))
            {
                var f = line.Split('\t');
                string At(string column)
                {
                    var i = Array.IndexOf(header, column);
                    return i >= 0 && i < f.Length ? f[i].Trim() : string.Empty;
                }

                if (!long.TryParse(At("ingredient_id"), NumberStyles.None, CultureInfo.InvariantCulture, out var id) || At("fdc_id").Length == 0) continue;
                requests.Add(new AttachRequest(id, At("name"), At("fdc_id")));
            }
            return requests;
        }

        /// <summary>
        /// Offers each named catalog food the identity and values of the USDA food named beside
        /// it, as a pending <see cref="AttachField"/> proposal (source fdc:&lt;id&gt;) that the review
        /// service applies in place: the food keeps its id, name and recipe lines. For a food
        /// whose everyday name is not the USDA description ("Dry White Wine" for "Alcoholic
        /// beverage, wine, table, white"). A request is passed over, with the reason counted, when
        /// the food is missing or goes by another name, already has a USDA record, the FDC id is
        /// held by or already offered to another food (retired ones included) or is not in the
        /// file, a proposal is already waiting, or the USDA values fail
        /// <see cref="FoodDataQualityValidator.Validate"/>.
        /// </summary>
        /// <param name="dryRun">Report what would be proposed without saving anything.</param>
        public async Task<AttachReport> AttachAsync(string csvDir, IReadOnlyList<AttachRequest> requests, bool dryRun, CancellationToken ct = default, string dataType = FoundationFood)
        {
            var report = new AttachReport { DryRun = dryRun, Requests = requests.Count };
            var fdcIds = requests.Select(r => r.FdcId).ToHashSet();
            var foods = ReadFoods(FindFile(csvDir, "food.csv"), dataType)
                .Where(f => fdcIds.Contains(f.Key)).ToDictionary(f => f.Key, f => f.Value);
            var nutrients = FdcNutrients.Read(FindFile(csvDir, "food_nutrient.csv"), foods.Keys.ToHashSet());
            var factors = ReadCalorieFactors(csvDir, foods.Keys.ToHashSet());
            var portionRows = UsdaPortionFile.Read(TryFindFile(csvDir, "food_portion.csv"), foods.Keys.ToHashSet());
            var portions = ReferenceServings(portionRows);
            var densities = Densities(portionRows);
            var itemWeights = ItemWeights(portionRows, foods);
            var targets = await FdcNutrients.ResolveAsync(_db, _logger, ct);
            report.UnresolvedNutrients.AddRange(FdcNutrients.All.Select(s => s.Key).Where(k => !targets.ContainsKey(k)));

            var ids = requests.Select(r => r.IngredientId).ToList();
            var catalog = await _db.Ingredients.Where(i => ids.Contains(i.Id) && !i.IsDeleted).ToDictionaryAsync(i => i.Id, ct);
            var pending = await _db.FoodCatalogProposals
                .Where(p => p.Field == AttachField && p.Status == FoodProposalStatus.Pending && p.IngredientId != null
                    && (ids.Contains(p.IngredientId.Value) || (p.FdcId != null && fdcIds.Contains(p.FdcId))))
                .Select(p => new { IngredientId = p.IngredientId!.Value, p.FdcId }).ToListAsync(ct);
            var waiting = pending.Select(p => p.IngredientId).ToHashSet();
            var holders = (await _db.Ingredients.IgnoreQueryFilters()
                .Where(i => i.FdcId != null && fdcIds.Contains(i.FdcId))
                .Select(i => new { IngredientId = i.Id, i.FdcId }).ToListAsync(ct))
                .Concat(pending.Where(p => p.FdcId != null))
                .GroupBy(h => h.FdcId!).ToDictionary(g => g.Key, g => g.Select(h => h.IngredientId).ToHashSet());

            var now = new DateTime(DateTime.UtcNow.Ticks / 10 * 10, DateTimeKind.Utc);
            foreach (var request in requests)
            {
                ct.ThrowIfCancellationRequested();
                void Skip(string reason)
                {
                    report.SkippedByReason[reason] = report.SkippedByReason.GetValueOrDefault(reason) + 1;
                    report.Lines.Add($"skipped  #{request.IngredientId} {request.Name}: {reason}");
                }

                if (!catalog.TryGetValue(request.IngredientId, out var food)) { Skip("food_not_found"); continue; }
                if (!string.Equals(food.Name.Trim(), request.Name.Trim(), StringComparison.OrdinalIgnoreCase)) { Skip("name_differs"); continue; }
                if (food.FdcId == request.FdcId) { Skip("already_attached"); continue; }
                if (food.FdcId != null) { Skip("has_usda_record"); continue; }
                if (waiting.Contains(food.Id)) { Skip("already_proposed"); continue; }
                if (holders.TryGetValue(request.FdcId, out var held) && held.Any(id => id != food.Id)) { Skip("fdc_id_taken"); continue; }
                if (!foods.TryGetValue(request.FdcId, out var usda)) { Skip("fdc_food_not_in_file"); continue; }
                if (!nutrients.TryGetValue(request.FdcId, out var m)) { Skip("nutrients_missing"); continue; }
                var verdict = _validator.Validate(m.QualityInput(usda.Description, UsedInSmallAmounts(usda), factors.GetValueOrDefault(request.FdcId)));
                if (!verdict.Accepted) { Skip(string.Join("+", verdict.Reasons)); continue; }

                decimal? serving = portions.TryGetValue(request.FdcId, out var sg) ? sg : null;
                decimal? density = densities.TryGetValue(request.FdcId, out var sd) ? sd : null;
                decimal? item = itemWeights.TryGetValue(request.FdcId, out var si) ? si : null;
                var proposal = AttachProposal(food.Id, food.Name, request.FdcId, usda.Description, dataType, m, serving, density, item, targets, now,
                    n => $"Named by the operator: USDA {dataType} food {request.FdcId} ({usda.Description}): attach its {n} nutrient values", out var values);
                waiting.Add(food.Id);
                holders[request.FdcId] = new HashSet<long> { food.Id };
                report.Proposals++;
                report.Lines.Add($"attach   #{food.Id} {food.Name} <- fdc:{request.FdcId} {usda.Description}: {values} values"
                    + (density != null ? $", {density} g/ml" : string.Empty)
                    + (item != null ? $", {item} g each" : string.Empty)
                    + (serving != null ? $", serving {serving} g" : string.Empty));
                if (!dryRun) _db.FoodCatalogProposals.Add(proposal);
            }

            if (!dryRun) await _db.SaveChangesAsync(ct);
            _logger.LogInformation("FDC attach{DryRun}: {Proposals} proposals for {Requests} requests.",
                dryRun ? " (dry run)" : string.Empty, report.Proposals, report.Requests);
            return report;
        }

        /// <summary>
        /// Takes back the attach of each named pair. An applied one is reversed from the audit
        /// journal the review service wrote (change type
        /// <see cref="FoodCatalogReviewService.FdcAttachChangeType"/>): every column the attach set
        /// goes back to its old value when it is unchanged since, the nutrient rows stamped with
        /// the FDC id and the USDA description alias are removed, the proposal is marked rejected
        /// and the food's recipe lines are marked changed. A proposal still pending is rejected.
        /// </summary>
        /// <param name="dryRun">Report what would be undone without saving anything.</param>
        public async Task<AttachReport> UndoAttachAsync(IReadOnlyList<AttachRequest> requests, bool dryRun, CancellationToken ct = default)
        {
            var report = new AttachReport { DryRun = dryRun, Requests = requests.Count };
            var ids = requests.Select(r => r.IngredientId).ToList();
            var catalog = await _db.Ingredients.Where(i => ids.Contains(i.Id) && !i.IsDeleted).ToDictionaryAsync(i => i.Id, ct);
            var journal = (await _db.AuditLogEntries
                .Where(a => a.EntityType == FoodCatalogReviewService.FdcAttachEntityType
                    && a.ChangeType == FoodCatalogReviewService.FdcAttachChangeType && ids.Contains(a.EntityId))
                .OrderByDescending(a => a.Id)
                .ToListAsync(ct))
                .ToLookup(a => a.EntityId);
            var proposals = (await _db.FoodCatalogProposals
                .Where(p => p.Field == AttachField && p.IngredientId != null && ids.Contains(p.IngredientId.Value)
                    && (p.Status == FoodProposalStatus.Pending || p.Status == FoodProposalStatus.Applied))
                .ToListAsync(ct))
                .ToLookup(p => (p.IngredientId!.Value, p.FdcId));
            var now = DateTime.UtcNow;
            var touched = new List<long>();

            await using var tx = !dryRun && _db.Database.IsRelational() ? await _db.Database.BeginTransactionAsync(ct) : null;
            foreach (var request in requests)
            {
                ct.ThrowIfCancellationRequested();
                void Skip(string reason)
                {
                    report.SkippedByReason[reason] = report.SkippedByReason.GetValueOrDefault(reason) + 1;
                    report.Lines.Add($"skipped  #{request.IngredientId} {request.Name}: {reason}");
                }

                if (!catalog.TryGetValue(request.IngredientId, out var food)) { Skip("food_not_found"); continue; }
                if (!string.Equals(food.Name.Trim(), request.Name.Trim(), StringComparison.OrdinalIgnoreCase)) { Skip("name_differs"); continue; }
                var mine = proposals[(food.Id, (string?)request.FdcId)].ToList();
                if (food.FdcId != request.FdcId)
                {
                    var waiting = mine.Where(p => p.Status == FoodProposalStatus.Pending).ToList();
                    if (waiting.Count == 0) { Skip("not_attached"); continue; }
                    if (!dryRun && await RejectIfPendingAsync(waiting, now, ct) < waiting.Count) { Skip("applied_meanwhile_rerun"); continue; }
                    report.Undone++;
                    report.Lines.Add($"rejected #{food.Id} {food.Name}: the pending proposal for fdc:{request.FdcId}");
                    continue;
                }
                var entries = journal[food.Id].ToList();
                if (!entries.Any(e => e.PropertyName == nameof(IngredientEntity.FdcId) && e.NewValue == request.FdcId)) { Skip("not_journaled"); continue; }

                var rows = await _db.Set<IngredientNutrientEntity>()
                    .Where(n => n.IngredientId == food.Id && n.FdcId == request.FdcId).ToListAsync(ct);
                var restored = new List<string>();
                var left = new List<string>();
                foreach (var entry in entries)
                {
                    if (entry.PropertyName == nameof(IngredientEntity.Aliases))
                    {
                        if (await RestoreAliasAsync(food.Id, entry, dryRun, ct)) restored.Add(entry.PropertyName);
                        else left.Add(entry.PropertyName);
                    }
                    else if (Restore(food, entry, dryRun))
                    {
                        restored.Add(entry.PropertyName!);
                    }
                    else
                    {
                        left.Add(entry.PropertyName!);
                    }
                }

                report.Undone++;
                report.Lines.Add($"undone   #{food.Id} {food.Name}: fdc:{request.FdcId} detached, {rows.Count} values removed, restored {string.Join(", ", restored)}"
                    + (left.Count > 0 ? $"; left as changed since: {string.Join(", ", left)}" : string.Empty));
                if (dryRun) continue;

                _db.Set<IngredientNutrientEntity>().RemoveRange(rows);
                _db.AuditLogEntries.RemoveRange(entries);
                Reject(mine, now);
                food.LastModifiedDate = now;
                touched.Add(food.Id);
            }

            if (dryRun) return report;
            await _db.SaveChangesAsync(ct);
            await TouchLinesAsync(touched, now, ct);
            if (tx != null) await tx.CommitAsync(ct);
            _logger.LogInformation("FDC attach undo: {Undone} of {Requests} requests.", report.Undone, report.Requests);
            return report;
        }

        /// <summary>
        /// Puts back the USDA description alias an attach journaled: one it added is removed, and
        /// one it found on the food (journaled as <see cref="FoodCatalogReviewService.AttachAliasBefore"/>)
        /// gets back its context and, when it had been removed, its removal. False when the alias
        /// no longer carries the attach's context or its journal cannot be read.
        /// </summary>
        private async Task<bool> RestoreAliasAsync(long foodId, AuditLogEntryEntity entry, bool dryRun, CancellationToken ct)
        {
            var alias = await _db.IngredientAliases.IgnoreQueryFilters().FirstOrDefaultAsync(a => a.IngredientId == foodId
                && a.AliasName == entry.NewValue && !a.IsDeleted && a.SourceContext == IngredientLinkService.UsdaDescriptionContext, ct);
            if (alias == null) return false;
            if (entry.OldValue == null)
            {
                if (!dryRun) _db.IngredientAliases.Remove(alias);
                return true;
            }

            FoodCatalogReviewService.AttachAliasBefore? was;
            try
            {
                was = System.Text.Json.JsonSerializer.Deserialize<FoodCatalogReviewService.AttachAliasBefore>(entry.OldValue);
            }
            catch (System.Text.Json.JsonException)
            {
                was = null;
            }
            if (was == null) return false;
            if (dryRun) return true;
            alias.SourceContext = was.Context;
            alias.IsDeleted = was.Deleted;
            alias.DeletedAt = was.DeletedAt;
            alias.DeletedByPersonId = was.DeletedByPersonId;
            alias.LastModifiedDate = DateTime.UtcNow;
            alias.LastModifiedByPersonId = SystemConstants.SystemPersonId;
            return true;
        }

        /// <summary>
        /// Rejects the proposals that are still pending when the write reaches the database, so an
        /// attach the certain-links lane applied after they were read keeps its Applied status.
        /// Returns how many were rejected.
        /// </summary>
        private async Task<int> RejectIfPendingAsync(List<FoodCatalogProposalEntity> proposals, DateTime now, CancellationToken ct)
        {
            if (_db.Database.IsRelational())
            {
                var ids = proposals.Select(p => p.Id).ToList();
                foreach (var p in proposals) _db.Entry(p).State = EntityState.Detached;
                return await _db.FoodCatalogProposals
                    .Where(p => ids.Contains(p.Id) && p.Status == FoodProposalStatus.Pending)
                    .ExecuteUpdateAsync(u => u
                        .SetProperty(p => p.Status, FoodProposalStatus.Rejected)
                        .SetProperty(p => p.ReviewedByPersonId, SystemConstants.SystemPersonId)
                        .SetProperty(p => p.ReviewedAt, now)
                        .SetProperty(p => p.LastModifiedDate, now), ct);
            }

            var rejected = 0;
            foreach (var p in proposals)
            {
                await _db.Entry(p).ReloadAsync(ct);
                if (p.Status != FoodProposalStatus.Pending) continue;
                Reject(new[] { p }, now);
                rejected++;
            }
            return rejected;
        }

        private static void Reject(IEnumerable<FoodCatalogProposalEntity> proposals, DateTime now)
        {
            foreach (var p in proposals)
            {
                p.Status = FoodProposalStatus.Rejected;
                p.ReviewedByPersonId = SystemConstants.SystemPersonId;
                p.ReviewedAt = now;
                p.LastModifiedDate = now;
            }
        }

        /// <summary>
        /// Puts one journaled column of a food back to its old value. False when the column no
        /// longer holds the value the attach gave it, so a later edit is never overwritten.
        /// </summary>
        private static bool Restore(IngredientEntity food, AuditLogEntryEntity entry, bool dryRun)
        {
            static decimal? Number(string? value) =>
                decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var n) ? n : null;

            switch (entry.PropertyName)
            {
                case nameof(IngredientEntity.FdcId):
                    if (food.FdcId != entry.NewValue) return false;
                    if (!dryRun) food.FdcId = entry.OldValue;
                    return true;
                case nameof(IngredientEntity.FdcDataType):
                    if (food.FdcDataType != entry.NewValue) return false;
                    if (!dryRun) food.FdcDataType = entry.OldValue ?? string.Empty;
                    return true;
                case nameof(IngredientEntity.CurationStatusId):
                    if (!long.TryParse(entry.OldValue, NumberStyles.None, CultureInfo.InvariantCulture, out var status)
                        || food.CurationStatusId.ToString(CultureInfo.InvariantCulture) != entry.NewValue) return false;
                    if (!dryRun) food.CurationStatusId = status;
                    return true;
                case nameof(IngredientEntity.ReferenceServingGrams):
                    if (food.ReferenceServingGrams != Number(entry.NewValue)) return false;
                    if (!dryRun) food.ReferenceServingGrams = Number(entry.OldValue);
                    return true;
                case nameof(IngredientEntity.GramsPerMilliliter):
                    if (food.GramsPerMilliliter != Number(entry.NewValue)) return false;
                    if (!dryRun) food.GramsPerMilliliter = Number(entry.OldValue);
                    return true;
                case nameof(IngredientEntity.GramsPerItem):
                    if (food.GramsPerItem != Number(entry.NewValue)) return false;
                    if (!dryRun) food.GramsPerItem = Number(entry.OldValue);
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// The pending proposal that carries one USDA food's identity and values to a catalog food.
        /// Each nutrient is a [nutrientId, amount, measurementId] triple, which keeps a food with
        /// every stored nutrient inside the proposal column.
        /// </summary>
        private FoodCatalogProposalEntity AttachProposal(
            long ingredientId, string currentName, string fdcId, string description, string dataType, FdcFoodNutrients m,
            decimal? serving, decimal? density, decimal? item, Dictionary<string, FdcNutrients.Target> targets, DateTime now,
            Func<int, string> reason, out int values)
        {
            var facts = new List<decimal[]>();
            foreach (var (key, amount) in FdcNutrients.Accepted(m, _validator))
                if (targets.TryGetValue(key, out var t))
                    facts.Add(new[] { t.NutrientId, amount, t.MeasurementId });

            values = facts.Count;
            return new FoodCatalogProposalEntity
            {
                Action = FoodProposalAction.Update,
                IngredientId = ingredientId,
                FdcId = fdcId,
                Field = AttachField,
                CurrentValue = currentName,
                ProposedValue = System.Text.Json.JsonSerializer.Serialize(new { fdcDataType = dataType, description, referenceServingGrams = serving, gramsPerMilliliter = density, gramsPerItem = item, nutrients = facts }),
                Confidence = 1m,
                Reason = reason(facts.Count),
                Source = $"fdc:{fdcId}",
                Batch = AttachBatch,
                Status = FoodProposalStatus.Pending,
                CreatedDate = now,
            };
        }

        private static AuditLogEntryEntity Journal(long ingredientId, string property, string? value, DateTime at) => new()
        {
            EntityType = ImportEntityType,
            EntityId = ingredientId,
            ChangeType = ImportChangeType,
            PropertyName = property,
            OldValue = null,
            NewValue = value,
            Timestamp = at,
            ChangedByPersonId = SystemConstants.SystemPersonId,
        };

        /// <summary>
        /// Marks the recipe lines of the given foods as changed, so the API's nutrition sweep
        /// recalculates the labels those lines count toward.
        /// </summary>
        private async Task TouchLinesAsync(List<long> ingredientIds, DateTime now, CancellationToken ct)
        {
            if (ingredientIds.Count == 0) return;
            if (_db.Database.IsRelational())
            {
                await _db.RecipeIngredients.Where(ri => ingredientIds.Contains(ri.IngredientId))
                    .ExecuteUpdateAsync(u => u.SetProperty(ri => ri.LastModifiedDate, now), ct);
                return;
            }
            foreach (var line in await _db.RecipeIngredients.Where(ri => ingredientIds.Contains(ri.IngredientId)).ToListAsync(ct))
            {
                line.LastModifiedDate = now;
            }
            await _db.SaveChangesAsync(ct);
        }

        private void AddNutrient(
            long ingredientId, Dictionary<string, FdcNutrients.Target> targets, string key,
            decimal amount, DateTime now, bool dryRun, ref int counter)
        {
            if (!targets.TryGetValue(key, out var target)) return;
            counter++;
            if (dryRun) return;
            _db.Set<IngredientNutrientEntity>().Add(new IngredientNutrientEntity
            {
                IngredientId = ingredientId,
                NutrientId = target.NutrientId,
                Amount = amount,
                MeasurementId = target.MeasurementId,
                CreatedDate = now,
            });
        }

        private static void CountDropped(ImportReport report, string reason) =>
            report.DroppedValuesByReason[reason] = report.DroppedValuesByReason.GetValueOrDefault(reason) + 1;

        /// <summary>
        /// Standard reference portion in grams per food. Prefers a single-unit portion
        /// (amount = 1), else the median gram weight, so an outlier ("1 whole cake") doesn't win.
        /// </summary>
        private static Dictionary<string, decimal> ReferenceServings(Dictionary<string, List<UsdaPortion>> portionRows)
        {
            var result = new Dictionary<string, decimal>();
            foreach (var (fdc, rows) in portionRows)
            {
                var list = rows.Where(x => x.Grams > 0).ToList();
                var single = list.Where(x => x.Amount == 1m).Select(x => x.Grams).OrderBy(g => g).ToList();
                var pool = single.Count > 0 ? single : list.Select(x => x.Grams).OrderBy(g => g).ToList();
                if (pool.Count == 0) continue;
                result[fdc] = pool[pool.Count / 2];
            }
            return result;
        }

        private static Dictionary<string, decimal> ItemWeights(Dictionary<string, List<UsdaPortion>> portionRows, Dictionary<string, FoodRow> foods)
        {
            var result = new Dictionary<string, decimal>();
            foreach (var (fdc, rows) in portionRows)
                if (UsdaCountWeight.GramsPerCount(rows, foods.TryGetValue(fdc, out var food) ? food.Description : null, food?.CategoryId) is { } item)
                    result[fdc] = item.Grams;
            return result;
        }

        private static Dictionary<string, decimal> Densities(Dictionary<string, List<UsdaPortion>> portionRows)
        {
            var result = new Dictionary<string, decimal>();
            foreach (var (fdc, list) in portionRows)
                if (UsdaVolumeDensity.GramsPerMilliliter(list) is { } d) result[fdc] = d;
            return result;
        }

        private sealed record FoodRow(string Description, int? CategoryId);

        private static bool UsedInSmallAmounts(FoodRow food) => CatalogAdmissionRule.UsedInSmallAmounts(food.CategoryId, food.Description);

        internal static Dictionary<string, CalorieFactors> ReadCalorieFactors(string csvDir, ISet<string> fdcIds) =>
            UsdaFoodFacts.CalorieFactors(TryFindFile(csvDir, "food_nutrient_conversion_factor.csv"), TryFindFile(csvDir, "food_calorie_conversion_factor.csv"), fdcIds);

        private static Dictionary<string, FoodRow> ReadFoods(string path, string dataType)
        {
            var result = new Dictionary<string, FoodRow>();
            using var reader = new StreamReader(path);
            reader.ReadLine(); // header: fdc_id,data_type,description,food_category_id,publication_date
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                var f = CsvLine.Split(line);
                if (f.Length < 4 || f[1] != dataType) continue;
                int? cat = int.TryParse(f[3], out var c) ? c : (int?)null;
                result[f[0]] = new FoodRow(f[2], cat);
            }
            return result;
        }

        private static string? TryFindFile(string dir, string name)
        {
            var direct = Path.Combine(dir, name);
            if (File.Exists(direct)) return direct;
            return Directory.GetFiles(dir, name, SearchOption.AllDirectories).FirstOrDefault();
        }

        private static string FindFile(string dir, string name)
        {
            if (File.Exists(Path.Combine(dir, name))) return Path.Combine(dir, name);
            var hit = Directory.GetFiles(dir, name, SearchOption.AllDirectories).FirstOrDefault();
            return hit ?? throw new FileNotFoundException($"{name} not found under {dir}");
        }

        private static string Truncate(string s, int max) => s.Length <= max ? s : s.Substring(0, max);


        public sealed class ImportReport
        {
            public bool DryRun { get; set; }
            public DateTime RunStamp { get; set; }
            public int TotalFoundation { get; set; }
            public int Accepted { get; set; }
            public int Classified { get; set; }
            /// <summary>Accepted foods the admission rule would curate, left pending for the gated `--admit` pass.</summary>
            public int Admissible { get; set; }
            /// <summary>Why the admission rule did not curate accepted foods, counted per reason.</summary>
            public Dictionary<string, int> HeldByReason { get; } = new();
            public int Rejected { get; set; }
            public int SkippedExisting { get; set; }
            public int SkippedRetired { get; set; }
            /// <summary>Foods an import undo retired that this run brings back, the same row with its nutrient rows.</summary>
            public int Revived { get; set; }
            public int SkippedDuplicateName { get; set; }
            public int WithReferenceServing { get; set; }
            public int WithDensity { get; set; }
            public int BackfilledDensities { get; set; }
            public int NutrientRows;
            public int BackfilledNutrientRows;
            public int AttachProposals { get; set; }
            public List<string> SkippedClaimed { get; } = new();
            public int SkippedNotCreated { get; set; }
            public Dictionary<string, int> BackfilledByNutrient { get; } = new();
            public Dictionary<string, int> RejectedByReason { get; } = new();
            public Dictionary<string, int> DroppedValuesByReason { get; } = new();
            public List<string> ListedNotInFile { get; } = new();
        }

        public sealed class ImportUndoReport
        {
            public bool DryRun { get; set; }
            public DateTime RunStamp { get; set; }
            public int FoodsRetired { get; set; }
            public int KeptInUse { get; set; }
            public int NutrientRowsRemoved { get; set; }
            public int DensitiesCleared { get; set; }
            public int ProposalsRejected { get; set; }
            public int Skipped { get; set; }
            public List<string> Lines { get; } = new();
        }

        public sealed class AttachReport
        {
            public bool DryRun { get; set; }
            public int Requests { get; set; }
            public int Proposals { get; set; }
            public int Undone { get; set; }
            public Dictionary<string, int> SkippedByReason { get; } = new();
            public List<string> Lines { get; } = new();
            public List<string> UnresolvedNutrients { get; } = new();
        }
    }
}

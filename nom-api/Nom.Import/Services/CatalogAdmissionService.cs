using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nom.Data;
using Nom.Data.Audit;
using Nom.Data.Curation;
using Nom.Data.Nutrition;
using Nom.Data.Recipe;
using Nom.Data.Reference;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;

namespace Nom.Import.Services
{
    /// <summary>
    /// The catalog admission pass over the USDA foods already in the catalog:
    /// `--admit [--foundation &lt;dir&gt;] [--sr &lt;dir&gt;] [--branded &lt;dir&gt;] --checksums &lt;file&gt; [--dry-run]`.
    /// It reads each food's USDA category, description, nutrients and own calorie factors from the
    /// USDA files, stores the category and the energy check, judges the food by
    /// <see cref="CatalogAdmissionRule"/>, sets its food group and edibility from the category alone,
    /// stores its USDA description as an <see cref="IngredientLinkService.UsdaDescriptionContext"/>
    /// alias, and withdraws the open model picks that point at a food an admin rejected. Only a
    /// PendingCuration food changes status, and only to Curated: a curated food the rule would not
    /// curate stays curated and is reported, and the rule never rejects a food. Before it writes, the run stops by itself, writing nothing, when a USDA file
    /// is not one recorded in the checksum file, a USDA food has no category, fewer than 99 % of the
    /// USDA foods pass the energy check, or more than <see cref="MaxChangedFoods"/> foods would
    /// change status, group or edibility. It also lays its changes over a copy of the catalog and
    /// runs the catalog audit on it: a food that would gain a new high or medium finding is held
    /// instead (status, group and edibility left as they are, the finding stored as
    /// <c>audit:&lt;code&gt;</c> in its admission reason), and the run stops, writing nothing, when it
    /// would hold more than <see cref="MaxAuditHoldShare"/> of the foods it changes. After it writes,
    /// it undoes itself when the audit still shows a high or medium finding on a food it admitted or
    /// regrouped that was not there before. Every change shares the run's stamp and is journaled; <see cref="UndoAsync"/> reverses a run,
    /// journaled under a stamp of its own that a later undo reverses in turn:
    /// `--admit --undo &lt;stamp&gt; [--dry-run]`.
    /// </summary>
    public class CatalogAdmissionService
    {
        public const string AdmitChangeType = "CatalogAdmit";
        public const string GroupChangeType = CatalogAdmissionRule.GroupChangeType;
        public const string UndoChangeType = "CatalogAdmitUndo";
        public const string FactsChangeType = "CatalogFacts";
        public const string DescriptionChangeType = "FdcDescription";
        public const string RunChangeType = "CatalogAdmitRun";
        public const string FoodEntityType = "Ingredient";
        public const string AliasEntityType = "IngredientAlias";
        public const string RunEntityType = "CatalogAdmission";
        public const int MaxChangedFoods = 10_000;
        public const decimal MinEnergyPassRate = 0.99m;
        public const decimal MaxAuditHoldShare = 0.01m;
        public const int MaxAuditPasses = 5;

        private static readonly string[] ChangeTypes = { AdmitChangeType, GroupChangeType, FactsChangeType, DescriptionChangeType, RunChangeType, UndoChangeType };

        private readonly ApplicationDbContext _db;
        private readonly ILogger<CatalogAdmissionService> _logger;
        private readonly FoodDataQualityValidator _validator = new();

        public CatalogAdmissionService(ApplicationDbContext db, ILogger<CatalogAdmissionService> logger)
        {
            _db = db;
            _logger = logger;
        }

        /// <summary>Where the pass reads the USDA files, the checksum file that vouches for them, whether it only reports, and how many foods it may change.</summary>
        public sealed record AdmitOptions(string? FoundationDir, string? SrLegacyDir, string? BrandedDir, string ChecksumFile, bool DryRun, int ChangeLimit = MaxChangedFoods);

        /// <summary>The key two food names share when they name the same food: the wording key of the link pipeline.</summary>
        public static string Key(string name) => IngredientNameNormalizer.WordingKey(name);

        /// <summary>
        /// The name keys of the live curated foods: each name and each USDA description alias, mapped
        /// to the food that holds it (the lowest id when two do).
        /// </summary>
        public static async Task<Dictionary<string, long>> CuratedKeysAsync(ApplicationDbContext db, CancellationToken ct)
        {
            var curated = (long)CurationStatusEnum.Curated;
            var names = await db.Ingredients.AsNoTracking()
                .Where(i => !i.IsDeleted && i.CurationStatusId == curated)
                .Select(i => new { i.Id, i.Name }).ToListAsync(ct);
            var descriptions = await db.IngredientAliases.AsNoTracking()
                .Where(a => !a.IsDeleted && a.SourceContext == IngredientLinkService.UsdaDescriptionContext
                    && !a.Ingredient.IsDeleted && a.Ingredient.CurationStatusId == curated)
                .Select(a => new { Id = a.IngredientId, Name = a.AliasName }).ToListAsync(ct);
            var keys = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var row in names.Concat(descriptions).OrderBy(r => r.Id))
                keys.TryAdd(Key(row.Name), row.Id);
            return keys;
        }

        private sealed record UsdaRow(string DataType, string Description, int? CategoryId);

        private sealed record Change(long FoodId, string ChangeType, string Property, string? Old, string? New);

        public async Task<AdmissionReport> AdmitAsync(AdmitOptions options, CancellationToken ct = default)
        {
            var report = new AdmissionReport { DryRun = options.DryRun };
            var recorded = ReadChecksums(options.ChecksumFile);
            var dirs = new[] { options.FoundationDir, options.SrLegacyDir }.Where(d => !string.IsNullOrWhiteSpace(d)).Select(d => d!).ToList();
            var sources = new List<string> { CatalogAdmissionRule.FoundationFood, CatalogAdmissionRule.SrLegacyFood };
            if (options.BrandedDir != null) sources.Add(CatalogAdmissionRule.BrandedFood);
            var before = await FindingsAsync(sources, null, ct);

            var foods = await _db.Ingredients
                .Where(i => !i.IsDeleted && i.FdcId != null
                    && (i.FdcDataType == CatalogAdmissionRule.FoundationFood || i.FdcDataType == CatalogAdmissionRule.SrLegacyFood
                        || (options.BrandedDir != null && i.FdcDataType == CatalogAdmissionRule.BrandedFood)))
                .OrderBy(i => i.Id)
                .ToListAsync(ct);
            var usdaIds = foods.Where(f => CatalogAdmissionRule.IsUsdaAuthored(f.FdcDataType)).Select(f => f.FdcId!).ToHashSet();
            var brandedIds = foods.Where(f => f.FdcDataType == CatalogAdmissionRule.BrandedFood).Select(f => f.FdcId!).ToHashSet();

            var usda = new Dictionary<string, UsdaRow>();
            var nutrients = new Dictionary<string, FdcFoodNutrients>();
            var factors = new Dictionary<string, CalorieFactors>();
            var categoryNames = new Dictionary<int, string>();
            foreach (var dir in dirs)
            {
                var foodCsv = Verify(FindFile(dir, "food.csv"), recorded, report);
                var rows = ReadUsdaFoods(foodCsv, usdaIds);
                foreach (var (id, row) in rows) usda[id] = row;
                foreach (var (id, n) in FdcNutrients.Read(Verify(FindFile(dir, "food_nutrient.csv"), recorded, report), rows.Keys.ToHashSet())) nutrients[id] = n;
                foreach (var (id, name) in UsdaFoodFacts.Categories(VerifyOptional(TryFindFile(dir, "food_category.csv"), recorded, report))) categoryNames[id] = name;
                foreach (var (id, f) in UsdaFoodFacts.CalorieFactors(
                    VerifyOptional(TryFindFile(dir, "food_nutrient_conversion_factor.csv"), recorded, report),
                    VerifyOptional(TryFindFile(dir, "food_calorie_conversion_factor.csv"), recorded, report), rows.Keys.ToHashSet()))
                    factors[id] = f;
            }

            var branded = new Dictionary<string, (string? Category, string? Description)>();
            if (options.BrandedDir != null && brandedIds.Count > 0)
            {
                var categories = ReadBrandedCategories(Verify(FindFile(options.BrandedDir, "branded_food.csv"), recorded, report), brandedIds);
                var descriptions = ReadUsdaFoods(Verify(FindFile(options.BrandedDir, "food.csv"), recorded, report), brandedIds);
                foreach (var id in brandedIds.Where(id => categories.ContainsKey(id) || descriptions.ContainsKey(id)))
                    branded[id] = (categories.GetValueOrDefault(id), descriptions.TryGetValue(id, out var d) ? d.Description : null);
                foreach (var (id, n) in FdcNutrients.Read(Verify(FindFile(options.BrandedDir, "food_nutrient.csv"), recorded, report), branded.Keys.ToHashSet())) nutrients[id] = n;
            }

            var curatedKeys = await CuratedKeysAsync(_db, ct);
            var foodIds = foods.Select(f => f.Id).ToList();
            var aliases = await _db.IngredientAliases.IgnoreQueryFilters()
                .Where(a => foodIds.Contains(a.IngredientId))
                .Select(a => new { a.IngredientId, a.AliasName, a.SourceContext, a.IsDeleted }).ToListAsync(ct);
            var describedIds = aliases.Where(a => a.SourceContext == IngredientLinkService.UsdaDescriptionContext && !a.IsDeleted)
                .Select(a => a.IngredientId).ToHashSet();
            var aliasKeys = aliases.Select(a => (a.IngredientId, a.AliasName)).ToHashSet();

            var judged = new List<Judged>();
            foreach (var food in foods)
            {
                ct.ThrowIfCancellationRequested();
                report.Candidates++;
                var isUsda = CatalogAdmissionRule.IsUsdaAuthored(food.FdcDataType);
                int? categoryId = null;
                string? category, description;
                if (isUsda)
                {
                    report.UsdaCandidates++;
                    if (!usda.TryGetValue(food.FdcId!, out var row))
                    {
                        report.CategoryMissing++;
                        report.NotInFile.Add($"#{food.Id} fdc:{food.FdcId} {food.Name}");
                        continue;
                    }
                    if (row.DataType != food.FdcDataType)
                    {
                        report.CategoryMissing++;
                        report.NotInFile.Add($"#{food.Id} fdc:{food.FdcId} {food.Name}: the file has it as {row.DataType}");
                        continue;
                    }
                    if (row.CategoryId == null) report.CategoryMissing++;
                    categoryId = row.CategoryId;
                    category = categoryId is int c ? categoryNames.GetValueOrDefault(c) : null;
                    description = row.Description;
                }
                else
                {
                    report.BrandedCandidates++;
                    if (!branded.TryGetValue(food.FdcId!, out var row))
                    {
                        report.BrandedNotInFile++;
                        continue;
                    }
                    category = string.IsNullOrWhiteSpace(row.Category) ? null : Truncate(row.Category.Trim(), 255);
                    description = row.Description;
                }

                var m = nutrients.GetValueOrDefault(food.FdcId!) ?? new FdcFoodNutrients(new Dictionary<string, decimal>());
                long? duplicate = null;
                foreach (var name in new[] { food.Name, description }.Where(n => !string.IsNullOrWhiteSpace(n)))
                    if (curatedKeys.TryGetValue(Key(name!), out var holder) && holder != food.Id) duplicate ??= holder;
                var decision = CatalogAdmissionRule.Decide(new AdmissionFacts(food.FdcDataType, categoryId, category, description ?? food.Name,
                    m.QualityInput(description ?? food.Name, factors: factors.GetValueOrDefault(food.FdcId!)), duplicate,
                    food.AgreeingSources, food.DisagreeingSources), _validator);

                if (isUsda)
                {
                    report.EnergyByOutcome[decision.EnergyCheck ?? "not_judged"] = report.EnergyByOutcome.GetValueOrDefault(decision.EnergyCheck ?? "not_judged") + 1;
                    if (decision.EnergyCheck is EnergyCheckOutcome.OwnFactors or EnergyCheckOutcome.General or EnergyCheckOutcome.ExemptSmall) report.EnergyPassed++;
                }
                judged.Add(new Judged(food, isUsda, categoryId, category, description, decision));
            }

            var described = new Described(describedIds, aliasKeys);
            var auditHolds = new Dictionary<long, string>();
            var plan = Build(judged, auditHolds, described, report);
            report.EnergyPassRate = report.UsdaCandidates == 0 ? 1m : Math.Round((decimal)report.EnergyPassed / report.UsdaCandidates, 4);
            if (report.ChecksumFailures.Count == 0 && report.CategoryMissing == 0)
            {
                for (var pass = 0; ; pass++)
                {
                    var fresh = (await FindingsAsync(sources, plan.Changes, ct))
                        .Where(f => plan.AdmittedOrRegrouped.Contains(f.FoodId) && !before.Contains(f))
                        .GroupBy(f => f.FoodId).ToList();
                    if (fresh.Count == 0) break;
                    if (pass == MaxAuditPasses)
                    {
                        report.Stopped.Add($"audit: holding {auditHolds.Count} food(s) for a new audit finding still leaves {fresh.Count} more after {MaxAuditPasses} passes");
                        break;
                    }
                    foreach (var food in fresh)
                        auditHolds[food.Key] = CatalogAdmissionRule.AuditFindingPrefix + string.Join("+", food.Select(f => f.Code).Distinct().OrderBy(c => c, StringComparer.Ordinal));
                    plan = Build(judged, auditHolds, described, report);
                }
            }

            report.DescriptionsAdded = plan.Descriptions.Count;
            report.ChangedFoods = plan.ChangedFoods.Count;
            var withdraw = await PicksToWithdrawAsync(plan.NotLinkTargets, ct);
            report.PicksWithdrawn = withdraw.Count;
            var auditHoldLimit = Math.Max(1, (int)Math.Floor((report.ChangedFoods + auditHolds.Count) * MaxAuditHoldShare));

            if (report.ChecksumFailures.Count > 0) report.Stopped.Add($"checksum: {report.ChecksumFailures.Count} USDA file(s) not recorded in {options.ChecksumFile}");
            if (report.CategoryMissing > 0) report.Stopped.Add($"category: {report.CategoryMissing} USDA food(s) without a category from the files");
            if (report.EnergyPassRate < MinEnergyPassRate) report.Stopped.Add($"energy: {report.EnergyPassRate:P2} of USDA foods pass the energy check, below {MinEnergyPassRate:P0}");
            if (report.ChangedFoods > options.ChangeLimit) report.Stopped.Add($"size: {report.ChangedFoods} foods would change, above {options.ChangeLimit}");
            if (auditHolds.Count > auditHoldLimit)
                report.Stopped.Add($"audit: {auditHolds.Count} foods would gain a new high or medium audit finding, more than the {auditHoldLimit} a run holds with a reason ({MaxAuditHoldShare:P0} of the foods it changes)");
            if (report.Stopped.Count > 0)
            {
                foreach (var reason in report.Stopped) _logger.LogWarning("Catalog admission stopped itself, nothing written: {Reason}", reason);
                return report;
            }
            if (options.DryRun) return report;

            var stamp = new DateTime(DateTime.UtcNow.Ticks / 10 * 10, DateTimeKind.Utc);
            report.RunStamp = stamp;
            await WriteAsync(foods.ToDictionary(f => f.Id), plan.Changes, plan.Descriptions, withdraw, stamp, ct);
            _logger.LogInformation("Catalog admission {Stamp:O}: {Admitted} admitted, {Held} held, {Regrouped} regrouped, {AuditHeld} held for an audit finding.", stamp, report.Admitted, report.Held, report.Regrouped, auditHolds.Count);

            var after = await FindingsAsync(sources, null, ct);
            foreach (var (foodId, code) in after.Where(f => plan.AdmittedOrRegrouped.Contains(f.FoodId) && !before.Contains(f)).OrderBy(f => f.FoodId))
                report.NewFindings.Add($"#{foodId}: {code}");
            if (report.NewFindings.Count > 0)
            {
                _logger.LogWarning("Catalog admission {Stamp:O} found {Count} new audit finding(s) on foods it admitted or regrouped and undoes itself.", stamp, report.NewFindings.Count);
                report.SelfUndo = await UndoAsync(stamp, dryRun: false, ct);
            }
            return report;
        }

        private sealed record Judged(IngredientEntity Food, bool IsUsda, int? CategoryId, string? Category, string? Description, AdmissionDecision Decision);

        private sealed record Described(HashSet<long> FoodIds, HashSet<(long FoodId, string Alias)> Aliases);

        private sealed record Plan(List<Change> Changes, List<(long FoodId, string Alias)> Descriptions, HashSet<long> ChangedFoods, HashSet<long> AdmittedOrRegrouped, HashSet<long> NotLinkTargets);

        /// <summary>
        /// The changes the run would write for the judged foods, counted into the report afresh. A
        /// food in <paramref name="auditHolds"/> keeps its status, group and edibility and stores the
        /// audit finding as its admission reason; its USDA facts and description are stored as for any food.
        /// </summary>
        private static Plan Build(List<Judged> judged, Dictionary<long, string> auditHolds, Described described, AdmissionReport report)
        {
            report.ResetPlan();
            var plan = new Plan(new List<Change>(), new List<(long, string)>(), new HashSet<long>(), new HashSet<long>(), new HashSet<long>());
            foreach (var (food, isUsda, categoryId, category, description, decision) in judged)
            {
                void Set(string changeType, string property, string? old, string? value)
                {
                    if (old == value) return;
                    plan.Changes.Add(new Change(food.Id, changeType, property, old, value));
                }

                var held = auditHolds.TryGetValue(food.Id, out var auditReason);
                if (isUsda) Set(FactsChangeType, nameof(IngredientEntity.FdcCategoryId), Int(food.FdcCategoryId), Int(categoryId));
                Set(FactsChangeType, nameof(IngredientEntity.FdcCategory), food.FdcCategory, category);
                Set(FactsChangeType, nameof(IngredientEntity.EnergyCheck), food.EnergyCheck, decision.EnergyCheck);
                Set(FactsChangeType, nameof(IngredientEntity.AdmissionReason), food.AdmissionReason, held ? auditReason : decision.Reason);

                if (held)
                {
                    var would = food.FoodGroupId != decision.FoodGroupId ? $"{GroupName(food.FoodGroupId)} -> {GroupName(decision.FoodGroupId)}" : "group unchanged";
                    report.HeldForAudit.Add($"#{food.Id} {food.Name}: {auditReason} (would be {(food.CurationStatusId == (long)CurationStatusEnum.PendingCuration && decision.Status == (long)CurationStatusEnum.Curated ? "admitted, " : string.Empty)}{would}; category {category ?? "none"})");
                    if (food.CurationStatusId == (long)CurationStatusEnum.PendingCuration)
                    {
                        report.Held++;
                        Count(report.HeldByReason, auditReason);
                    }
                }
                else
                {
                    if (food.FoodGroupId != decision.FoodGroupId)
                    {
                        Set(GroupChangeType, nameof(IngredientEntity.FoodGroupId), Long(food.FoodGroupId), Long(decision.FoodGroupId));
                        report.Regrouped++;
                        if (decision.FoodGroupId == null) report.GroupsCleared++;
                        var move = $"{GroupName(food.FoodGroupId)} -> {GroupName(decision.FoodGroupId)}";
                        report.GroupMoves[move] = report.GroupMoves.GetValueOrDefault(move) + 1;
                        if (report.Lines.Count < 400) report.Lines.Add($"regroup  #{food.Id} {food.Name}: {move} (category {category ?? "none"})");
                        plan.ChangedFoods.Add(food.Id);
                        plan.AdmittedOrRegrouped.Add(food.Id);
                    }
                    if (decision.IsWholeFood is { } edible && food.IsWholeFood != edible)
                    {
                        Set(GroupChangeType, nameof(IngredientEntity.IsWholeFood), Bool(food.IsWholeFood), Bool(edible));
                        report.EdibilitySet++;
                        plan.ChangedFoods.Add(food.Id);
                    }

                    if (food.CurationStatusId == (long)CurationStatusEnum.PendingCuration && decision.Status == (long)CurationStatusEnum.Curated)
                    {
                        Set(AdmitChangeType, nameof(IngredientEntity.CurationStatusId), Long(food.CurationStatusId), Long(decision.Status));
                        plan.ChangedFoods.Add(food.Id);
                        report.Admitted++;
                        if (decision.Prepared) report.AdmittedPrepared++;
                        plan.AdmittedOrRegrouped.Add(food.Id);
                        if (report.Lines.Count < 400) report.Lines.Add($"admit    #{food.Id} {food.Name} ({decision.EnergyCheck}{(decision.Prepared ? ", prepared" : string.Empty)})");
                    }
                    else if (food.CurationStatusId == (long)CurationStatusEnum.PendingCuration)
                    {
                        report.Held++;
                        foreach (var reason in decision.Reasons) Count(report.HeldByReason, Bucket(reason));
                    }
                    else if (food.CurationStatusId == (long)CurationStatusEnum.Curated && decision.Status != (long)CurationStatusEnum.Curated)
                    {
                        report.KeptCurated++;
                        foreach (var reason in decision.Reasons) Count(report.KeptCuratedByReason, Bucket(reason));
                        if (report.Lines.Count < 400) report.Lines.Add($"kept     #{food.Id} {food.Name}: curated, the rule says {decision.Reason}");
                    }
                }
                if (food.CurationStatusId == (long)CurationStatusEnum.Rejected || decision.Prepared) plan.NotLinkTargets.Add(food.Id);

                if (isUsda && !string.IsNullOrWhiteSpace(description) && !described.FoodIds.Contains(food.Id))
                {
                    var alias = Truncate(description.Trim(), 511);
                    if (described.Aliases.Contains((food.Id, alias))) report.DescriptionsBlocked++;
                    else plan.Descriptions.Add((food.Id, alias));
                }
            }
            return plan;
        }

        /// <summary>
        /// Takes back what the run stamped <paramref name="stamp"/> wrote: every journaled value is
        /// put back where the food still holds the value the run gave it (one changed since is left
        /// alone and reported), the USDA description aliases it added are removed, and the model
        /// picks it withdrew are restored. A food it admitted that a recipe line uses now is demoted
        /// all the same and reported as in use; recipes already approved are not changed. The run's
        /// journal is kept, and each value the undo puts back is journaled as
        /// <see cref="UndoChangeType"/> under the undo's own stamp, so the undo can itself be undone.
        /// The run may be an admission run, an undo, or a category grouping that journals
        /// <see cref="GroupChangeType"/>.
        /// </summary>
        public async Task<AdmissionUndoReport> UndoAsync(DateTime stamp, bool dryRun, CancellationToken ct = default)
        {
            var report = new AdmissionUndoReport { DryRun = dryRun, RunStamp = stamp };
            var entries = await _db.AuditLogEntries
                .Where(a => a.Timestamp == stamp
                    && ((a.EntityType == FoodEntityType || a.EntityType == AliasEntityType || a.EntityType == RunEntityType) && ChangeTypes.Contains(a.ChangeType)
                        || (a.EntityType == LineRelinkService.PickWithdrawEntityType && (a.ChangeType == LineRelinkService.ProposalWithdrawChangeType || a.ChangeType == UndoChangeType))))
                .OrderByDescending(a => a.Id)
                .ToListAsync(ct);
            var foodIds = entries.Where(e => e.EntityType == FoodEntityType).Select(e => e.EntityId).Distinct().ToList();
            var foods = await _db.Ingredients.IgnoreQueryFilters().Where(i => foodIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
            var inUse = (await _db.RecipeIngredients.Where(ri => foodIds.Contains(ri.IngredientId))
                .Select(ri => ri.IngredientId).Distinct().ToListAsync(ct)).ToHashSet();
            var now = new DateTime(DateTime.UtcNow.Ticks / 10 * 10, DateTimeKind.Utc);
            if (now <= stamp) now = stamp.AddTicks(10);
            var undone = new List<AuditLogEntryEntity>();

            await using var tx = !dryRun && _db.Database.IsRelational() ? await _db.Database.BeginTransactionAsync(ct) : null;
            foreach (var entry in entries)
            {
                ct.ThrowIfCancellationRequested();
                if (entry.EntityType == RunEntityType)
                    continue;
                if (entry.EntityType == AliasEntityType)
                {
                    if (entry.NewValue != null)
                    {
                        var alias = await _db.IngredientAliases.IgnoreQueryFilters()
                            .FirstOrDefaultAsync(a => a.IngredientId == entry.EntityId && a.AliasName == entry.NewValue, ct);
                        if (alias == null || alias.SourceContext != IngredientLinkService.UsdaDescriptionContext)
                        {
                            report.Skipped++;
                            continue;
                        }
                        report.DescriptionsRemoved++;
                        if (!dryRun) _db.IngredientAliases.Remove(alias);
                    }
                    else
                    {
                        if (entry.OldValue == null || await _db.IngredientAliases.IgnoreQueryFilters()
                            .AnyAsync(a => a.IngredientId == entry.EntityId && a.AliasName == entry.OldValue, ct))
                        {
                            report.Skipped++;
                            continue;
                        }
                        report.DescriptionsRestored++;
                        if (!dryRun)
                        {
                            _db.IngredientAliases.Add(new IngredientAliasEntity
                            {
                                IngredientId = entry.EntityId,
                                AliasName = entry.OldValue,
                                SourceContext = IngredientLinkService.UsdaDescriptionContext,
                                CreatedDate = now,
                                CreatedByPersonId = SystemConstants.SystemPersonId,
                            });
                        }
                    }
                    undone.Add(entry);
                    continue;
                }
                if (entry.EntityType == LineRelinkService.PickWithdrawEntityType)
                {
                    var withdrawn = entry.NewValue == bool.TrueString;
                    var pick = await _db.FoodCatalogProposals.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == entry.EntityId, ct);
                    if (pick == null || pick.IsDeleted != withdrawn)
                    {
                        report.Skipped++;
                        continue;
                    }
                    if (withdrawn) report.PicksRestored++;
                    else report.PicksWithdrawn++;
                    if (!dryRun)
                    {
                        pick.IsDeleted = !withdrawn;
                        pick.DeletedAt = withdrawn ? null : now;
                        pick.DeletedByPersonId = withdrawn ? null : SystemConstants.SystemPersonId;
                    }
                    undone.Add(entry);
                    continue;
                }

                if (!foods.TryGetValue(entry.EntityId, out var food) || ReadValue(food, entry.PropertyName) != entry.NewValue)
                {
                    report.Skipped++;
                    report.Lines.Add($"left     #{entry.EntityId} {food?.Name}: {entry.PropertyName} changed since the run");
                    continue;
                }
                if (entry.ChangeType == AdmitChangeType)
                {
                    report.Demoted++;
                    if (inUse.Contains(food.Id)) report.Lines.Add($"in use   #{food.Id} {food.Name}: demoted while a recipe line uses it");
                }
                else if (entry.ChangeType == UndoChangeType) report.Reapplied++;
                else if (entry.ChangeType == GroupChangeType) report.GroupsRestored++;
                else report.FactsRestored++;
                if (!dryRun)
                {
                    WriteValue(food, entry.PropertyName, entry.OldValue);
                    food.LastModifiedDate = now;
                }
                undone.Add(entry);
            }

            if (dryRun) return report;
            report.UndoStamp = now;
            _db.AuditLogEntries.Add(Journal(RunEntityType, 0, UndoChangeType, "UndoOf", null, stamp.ToString("O", CultureInfo.InvariantCulture), now));
            foreach (var entry in undone)
                _db.AuditLogEntries.Add(Journal(entry.EntityType, entry.EntityId, UndoChangeType, entry.PropertyName, entry.NewValue, entry.OldValue, now));
            await _db.SaveChangesAsync(ct);
            if (tx != null) await tx.CommitAsync(ct);
            _logger.LogInformation("Catalog admission undo {Stamp:O} as {UndoStamp:O}: {Demoted} demoted, {Groups} groups restored, {Reapplied} values of an earlier undo taken back.", stamp, now, report.Demoted, report.GroupsRestored, report.Reapplied);
            return report;
        }

        private async Task WriteAsync(Dictionary<long, IngredientEntity> foods, List<Change> changes, List<(long FoodId, string Alias)> descriptions,
            List<FoodCatalogProposalEntity> withdraw, DateTime stamp, CancellationToken ct)
        {
            await using var tx = _db.Database.IsRelational() ? await _db.Database.BeginTransactionAsync(ct) : null;
            _db.AuditLogEntries.Add(Journal(RunEntityType, 0, RunChangeType, "RuleVersion", null, CatalogAdmissionRule.Version, stamp));
            foreach (var change in changes)
            {
                var food = foods[change.FoodId];
                WriteValue(food, change.Property, change.New);
                food.LastModifiedDate = stamp;
                _db.AuditLogEntries.Add(Journal(FoodEntityType, change.FoodId, change.ChangeType, change.Property, change.Old, change.New, stamp));
            }
            foreach (var (foodId, alias) in descriptions)
            {
                _db.IngredientAliases.Add(new IngredientAliasEntity
                {
                    IngredientId = foodId,
                    AliasName = alias,
                    SourceContext = IngredientLinkService.UsdaDescriptionContext,
                    CreatedDate = stamp,
                    CreatedByPersonId = SystemConstants.SystemPersonId,
                });
                _db.AuditLogEntries.Add(Journal(AliasEntityType, foodId, DescriptionChangeType, nameof(IngredientAliasEntity.AliasName), null, alias, stamp));
            }
            foreach (var pick in withdraw)
            {
                pick.IsDeleted = true;
                pick.DeletedAt = stamp;
                pick.DeletedByPersonId = SystemConstants.SystemPersonId;
                _db.AuditLogEntries.Add(Journal(LineRelinkService.PickWithdrawEntityType, pick.Id, LineRelinkService.ProposalWithdrawChangeType,
                    nameof(FoodCatalogProposalEntity.IsDeleted), bool.FalseString, bool.TrueString, stamp));
            }
            await _db.SaveChangesAsync(ct);
            if (tx != null) await tx.CommitAsync(ct);
        }

        /// <summary>The open model link picks (source ai:) that point at a food an admin rejected or at a prepared food.</summary>
        private async Task<List<FoodCatalogProposalEntity>> PicksToWithdrawAsync(HashSet<long> notTargets, CancellationToken ct)
        {
            if (notTargets.Count == 0) return new List<FoodCatalogProposalEntity>();
            var targets = notTargets.Select(id => id.ToString(CultureInfo.InvariantCulture)).ToList();
            return await _db.FoodCatalogProposals
                .Where(p => p.Field == FoodCatalogReviewService.FdcLinkField && p.Status == FoodProposalStatus.Pending
                    && p.Source.StartsWith("ai:") && p.ProposedValue != null && targets.Contains(p.ProposedValue))
                .ToListAsync(ct);
        }

        /// <summary>
        /// The high and medium audit findings over the USDA sources, by food and code: as the catalog
        /// stands, or with <paramref name="planned"/> laid over an untracked copy of it, so a run can see
        /// the audit after its write before it writes.
        /// </summary>
        private async Task<HashSet<(long FoodId, string Code)>> FindingsAsync(IReadOnlyCollection<string> sources, IReadOnlyCollection<Change>? planned, CancellationToken ct)
        {
            var result = new HashSet<(long, string)>();
            var audit = new FoodCatalogAuditService(_db);
            var byFood = (planned ?? Array.Empty<Change>()).ToLookup(c => c.FoodId);
            foreach (var source in sources)
            {
                ct.ThrowIfCancellationRequested();
                var population = await audit.PopulationAsync(source, ct);
                foreach (var food in population)
                {
                    foreach (var change in byFood[food.Id])
                    {
                        WriteValue(food, change.Property, change.New);
                        if (change.Property == nameof(IngredientEntity.FoodGroupId)) food.FoodGroup = null;
                    }
                }
                foreach (var f in FoodCatalogAuditService.Findings(population).Where(f => f.Severity is "high" or "medium"))
                    result.Add((f.IngredientId, f.Code));
            }
            return result;
        }

        private static AuditLogEntryEntity Journal(string entityType, long entityId, string changeType, string? property, string? old, string? value, DateTime at) => new()
        {
            EntityType = entityType,
            EntityId = entityId,
            ChangeType = changeType,
            PropertyName = property,
            OldValue = old,
            NewValue = value,
            Timestamp = at,
            ChangedByPersonId = SystemConstants.SystemPersonId,
        };

        private static string? ReadValue(IngredientEntity food, string? property) => property switch
        {
            nameof(IngredientEntity.CurationStatusId) => Long(food.CurationStatusId),
            nameof(IngredientEntity.FoodGroupId) => Long(food.FoodGroupId),
            nameof(IngredientEntity.IsWholeFood) => Bool(food.IsWholeFood),
            nameof(IngredientEntity.FdcCategoryId) => Int(food.FdcCategoryId),
            nameof(IngredientEntity.FdcCategory) => food.FdcCategory,
            nameof(IngredientEntity.EnergyCheck) => food.EnergyCheck,
            nameof(IngredientEntity.AdmissionReason) => food.AdmissionReason,
            _ => "\u0000",
        };

        private static void WriteValue(IngredientEntity food, string? property, string? value)
        {
            switch (property)
            {
                case nameof(IngredientEntity.CurationStatusId):
                    food.CurationStatusId = long.Parse(value!, NumberStyles.Integer, CultureInfo.InvariantCulture);
                    break;
                case nameof(IngredientEntity.FoodGroupId):
                    food.FoodGroupId = value == null ? null : long.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
                    break;
                case nameof(IngredientEntity.IsWholeFood):
                    food.IsWholeFood = value == null ? null : bool.Parse(value);
                    break;
                case nameof(IngredientEntity.FdcCategoryId):
                    food.FdcCategoryId = value == null ? null : int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
                    break;
                case nameof(IngredientEntity.FdcCategory):
                    food.FdcCategory = value;
                    break;
                case nameof(IngredientEntity.EnergyCheck):
                    food.EnergyCheck = value;
                    break;
                case nameof(IngredientEntity.AdmissionReason):
                    food.AdmissionReason = value;
                    break;
                default:
                    throw new InvalidOperationException($"The admission journal names no column {property}.");
            }
        }

        private static string? Long(long? value) => value?.ToString(CultureInfo.InvariantCulture);

        private static string? Int(int? value) => value?.ToString(CultureInfo.InvariantCulture);

        private static string? Bool(bool? value) => value?.ToString();

        private static string GroupName(long? group) => group is long g && Enum.IsDefined(typeof(FoodGroupEnum), g) ? ((FoodGroupEnum)g).ToString() : group?.ToString(CultureInfo.InvariantCulture) ?? "none";

        private static string Bucket(string reason) =>
            reason.StartsWith(CatalogAdmissionRule.DuplicatePrefix, StringComparison.Ordinal) ? "duplicate" : reason;

        private static void Count(Dictionary<string, int> counts, string? key)
        {
            if (key == null) return;
            counts[key] = counts.GetValueOrDefault(key) + 1;
        }

        /// <summary>
        /// Reads a checksum file in sha256sum format ("&lt;sha256&gt;  &lt;path&gt;", '#' starts a comment):
        /// each file name with every hash recorded for it, across USDA releases.
        /// </summary>
        public static Dictionary<string, HashSet<string>> ReadChecksums(string path)
        {
            var result = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            if (!File.Exists(path)) throw new FileNotFoundException($"Checksum file {path} not found.");
            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith('#')) continue;
                var parts = line.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 2) continue;
                var name = Path.GetFileName(parts[1].TrimStart('*').Trim());
                if (!result.TryGetValue(name, out var hashes)) result[name] = hashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                hashes.Add(parts[0]);
            }
            return result;
        }

        public static string Sha256(string path)
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }

        private static string Verify(string path, Dictionary<string, HashSet<string>> recorded, AdmissionReport report)
        {
            var hash = Sha256(path);
            report.Checksums.Add($"{hash}  {path}");
            if (!recorded.TryGetValue(Path.GetFileName(path), out var hashes) || !hashes.Contains(hash))
                report.ChecksumFailures.Add($"{path} (sha256 {hash})");
            return path;
        }

        private static string? VerifyOptional(string? path, Dictionary<string, HashSet<string>> recorded, AdmissionReport report) =>
            path == null ? null : Verify(path, recorded, report);

        private static Dictionary<string, UsdaRow> ReadUsdaFoods(string path, ISet<string> fdcIds)
        {
            var result = new Dictionary<string, UsdaRow>();
            using var reader = new StreamReader(path);
            reader.ReadLine();
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                var f = CsvLine.Split(line);
                if (f.Length < 3 || !fdcIds.Contains(f[0])) continue;
                result[f[0]] = new UsdaRow(f[1], f[2], f.Length > 3 && int.TryParse(f[3], NumberStyles.None, CultureInfo.InvariantCulture, out var c) ? c : null);
            }
            return result;
        }

        private static Dictionary<string, string?> ReadBrandedCategories(string path, ISet<string> fdcIds)
        {
            var result = new Dictionary<string, string?>();
            using var reader = new StreamReader(path);
            var header = CsvLine.Split(reader.ReadLine() ?? string.Empty);
            var iFdc = Array.FindIndex(header, h => h.Equals("fdc_id", StringComparison.OrdinalIgnoreCase));
            var iCategory = Array.FindIndex(header, h => h.Equals("branded_food_category", StringComparison.OrdinalIgnoreCase));
            if (iFdc < 0) return result;
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                var f = CsvLine.Split(line);
                if (f.Length <= iFdc || !fdcIds.Contains(f[iFdc])) continue;
                result[f[iFdc]] = iCategory >= 0 && f.Length > iCategory ? f[iCategory] : null;
            }
            return result;
        }

        private static string? TryFindFile(string dir, string name)
        {
            var direct = Path.Combine(dir, name);
            if (File.Exists(direct)) return direct;
            return Directory.GetFiles(dir, name, SearchOption.AllDirectories).FirstOrDefault();
        }

        private static string FindFile(string dir, string name) =>
            TryFindFile(dir, name) ?? throw new FileNotFoundException($"{name} not found under {dir}");

        private static string Truncate(string s, int max) => s.Length <= max ? s : s.Substring(0, max);

        public sealed class AdmissionReport
        {
            public bool DryRun { get; set; }
            public DateTime RunStamp { get; set; }
            public string RuleVersion { get; } = CatalogAdmissionRule.Version;
            public int Candidates { get; set; }
            public int UsdaCandidates { get; set; }
            public int BrandedCandidates { get; set; }
            public int BrandedNotInFile { get; set; }
            public int CategoryMissing { get; set; }
            public int Admitted { get; set; }
            public int AdmittedPrepared { get; set; }
            public int Held { get; set; }
            public int KeptCurated { get; set; }
            public int Regrouped { get; set; }
            public int GroupsCleared { get; set; }
            public int EdibilitySet { get; set; }
            public int DescriptionsAdded { get; set; }
            public int DescriptionsBlocked { get; set; }
            public int PicksWithdrawn { get; set; }
            public int EnergyPassed { get; set; }
            public decimal EnergyPassRate { get; set; }
            public int ChangedFoods { get; set; }
            public Dictionary<string, int> EnergyByOutcome { get; } = new();
            public Dictionary<string, int> HeldByReason { get; } = new();
            public Dictionary<string, int> KeptCuratedByReason { get; } = new();
            public Dictionary<string, int> GroupMoves { get; } = new();
            public List<string> NotInFile { get; } = new();
            public List<string> Checksums { get; } = new();
            public List<string> ChecksumFailures { get; } = new();
            public List<string> Stopped { get; } = new();
            public List<string> NewFindings { get; } = new();
            public List<string> HeldForAudit { get; } = new();
            public List<string> Lines { get; } = new();
            public AdmissionUndoReport? SelfUndo { get; set; }

            internal void ResetPlan()
            {
                Admitted = AdmittedPrepared = Held = KeptCurated = Regrouped = GroupsCleared = EdibilitySet = DescriptionsBlocked = 0;
                HeldByReason.Clear();
                KeptCuratedByReason.Clear();
                GroupMoves.Clear();
                HeldForAudit.Clear();
                Lines.Clear();
            }
        }

        public sealed class AdmissionUndoReport
        {
            public bool DryRun { get; set; }
            public DateTime RunStamp { get; set; }
            public DateTime? UndoStamp { get; set; }
            public int Demoted { get; set; }
            public int Reapplied { get; set; }
            public int GroupsRestored { get; set; }
            public int FactsRestored { get; set; }
            public int DescriptionsRemoved { get; set; }
            public int DescriptionsRestored { get; set; }
            public int PicksRestored { get; set; }
            public int PicksWithdrawn { get; set; }
            public int Skipped { get; set; }
            public List<string> Lines { get; } = new();
        }
    }
}

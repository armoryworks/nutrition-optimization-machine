using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Nom.Data;
using Nom.Data.Audit;
using Nom.Data.Measurement;
using Nom.Data.Nutrient;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Recipe;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;
using Nom.Orch.UtilityInterfaces;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// A generated copy of the order-only pass as staging ran it (checkpoint 4, N33): recipes with lost
    /// order and a stored source, rebuilds, rule rejections and second copies journaled around the pass,
    /// and the later steps that make an undo follow, skip or put a line at the next free position, then a
    /// catalog fill that changes foods' nutrients with no label calculated again (CK9US-1). The
    /// same seed gives the same rows, ids and journal on every run, so two copies can be undone two ways
    /// and compared row for row (<see cref="DumpAsync"/>).
    /// </summary>
    internal static class ReprocessUndoWorld
    {
        public const long Operator = 2;
        public const long Gram = 1, Cup = 2, Piece = 3;
        public static readonly DateTime Imported = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        public static readonly DateTime Since = new(2026, 9, 15, 0, 0, 0, DateTimeKind.Utc);
        public static readonly DateTime BeforePass = new(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc);
        public static readonly DateTime AfterPass = new(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc);
        public const string CraftedBatch = "rp-crafted-rebuild";
        public const string RunEntityType = "RecipeReprocessUndoRun";

        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        public sealed class Admin : ICurrentUserService
        {
            public long? PersonId => Operator;
            public long PersonIdOrSystem => Operator;
            public long RequiredPersonId => Operator;
            public string? UserId => "operator";
            public string RequiredUserId => "operator";
        }

        public sealed class World
        {
            public required ApplicationDbContext Db { get; init; }
            public required DateTime PassStarted { get; init; }
            public required List<long> RecipeIds { get; init; }
            public required string Store { get; init; }

            public ApplicationDbContext OtherContext() => NewDb(Store);
        }

        public static ApplicationDbContext NewDb(string? store = null) =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(store ?? "reprocess-undo-world-" + Guid.NewGuid().ToString("N"))
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options);

        public static RecipeReprocessService Service(ApplicationDbContext db) =>
            new(db, new Admin(), new Mock<IRecipeScraperClient>().Object, new RecipeVettingService(),
                new RecipeNutritionService(db, NullLogger<RecipeNutritionService>.Instance), NullLogger<RecipeReprocessService>.Instance);

        public static async Task<World> GenerateAsync(int recipes, int seed)
        {
            var rng = new Random(seed);
            var store = "reprocess-undo-world-" + Guid.NewGuid().ToString("N");
            var db = NewDb(store);
            db.Measurements.AddRange(
                new BaseMeasurementEntity { Id = Gram, Name = "Gram", Symbol = "g", MeasurementCategoryId = 1, BaseUnitConversionFactor = 1m },
                new BaseMeasurementEntity { Id = Cup, Name = "Cup", Symbol = "cup", MeasurementCategoryId = 2, BaseUnitConversionFactor = 240m },
                new BaseMeasurementEntity { Id = Piece, Name = "Piece", Symbol = "pc", MeasurementCategoryId = 3, BaseUnitConversionFactor = 1m });
            db.Nutrients.AddRange(
                new NutrientEntity { Id = 1, Name = "Calories", DefaultMeasurementId = Gram },
                new NutrientEntity { Id = 2, Name = "Protein", DefaultMeasurementId = Gram },
                new NutrientEntity { Id = 3, Name = "Total Fat", DefaultMeasurementId = Gram },
                new NutrientEntity { Id = 4, Name = "Iron", DefaultMeasurementId = Gram });
            await db.SaveChangesAsync();

            var foods = new List<IngredientEntity>();
            for (var i = 0; i < 30; i++)
            {
                var food = new IngredientEntity
                {
                    Name = "food " + i.ToString(CultureInfo.InvariantCulture),
                    CurationStatusId = (long)CurationStatusEnum.Curated,
                    GramsPerMilliliter = 0.5m + rng.Next(0, 70) / 100m,
                    GramsPerItem = rng.Next(0, 2) == 0 ? null : rng.Next(20, 200),
                    CreatedDate = Imported,
                    IngredientNutrients = Enumerable.Range(1, 3)
                        .Select(n => new IngredientNutrientEntity { NutrientId = n, Amount = rng.Next(1, 5000) / 10m, MeasurementId = Gram, CreatedDate = Imported })
                        .ToList(),
                };
                db.Ingredients.Add(food);
                foods.Add(food);
            }
            await db.SaveChangesAsync();

            var ids = new List<long>();
            for (var i = 0; i < recipes; i++)
            {
                var count = rng.Next(3, 8);
                var chosen = foods.OrderBy(_ => rng.Next()).Take(count).ToList();
                var rows = chosen.Select((food, j) =>
                {
                    var unit = rng.Next(0, 3) switch { 0 => Gram, 1 => Cup, _ => Piece };
                    var quantity = unit == Gram ? rng.Next(5, 500) : rng.Next(1, 8) / 2m;
                    return new RecipeIngredientEntity
                    {
                        IngredientId = food.Id, SortOrder = 0, Quantity = quantity, MeasurementId = unit,
                        RawLine = string.Create(CultureInfo.InvariantCulture, $"{quantity} {(unit == Gram ? "g" : unit == Cup ? "cups" : "")} {food.Name} ({i}.{j})"),
                        CreatedDate = Imported, CreatedByPersonId = Operator,
                    };
                }).ToList();
                var source = rows.Select(r => r.RawLine).OrderBy(_ => rng.Next()).ToList();
                if (rng.Next(0, 20) == 0) source.RemoveAt(0);

                var record = new ScraperRecipe
                {
                    Name = "Recipe " + i.ToString(CultureInfo.InvariantCulture),
                    Ingredients = source.Select(l => new ScraperIngredient { RawLine = l }).ToList(),
                    Steps = new List<ScraperStep> { new() { Order = 1, Instruction = "Mix everything well in a large bowl." } },
                };
                var recipe = new RecipeEntity
                {
                    Name = record.Name,
                    CurationStatusId = rng.Next(0, 6) == 0 ? (long)CurationStatusEnum.Curated : (long)CurationStatusEnum.NonCurated,
                    ScrapedAtUtc = Imported,
                    LicenseStatus = RecipeLicenseStatus.PublicDomain,
                    Servings = rng.Next(1, 7),
                    CreatedDate = Imported,
                    RecipeIngredients = rows,
                    RecipeSteps = new List<RecipeStepEntity> { new() { StepNumber = 1, Summary = "Mix", Description = "Mix everything well in a large bowl.", CreatedDate = Imported } },
                };
                db.Recipes.Add(recipe);
                await db.SaveChangesAsync();
                var content = SourceRecord.Content(record);
                db.ScrapedDocuments.Add(new ScrapedDocumentEntity
                {
                    RecipeId = recipe.Id, RawJsonLd = content, Format = SourceRecord.Format(record), ContentSha256 = SourceRecord.Sha256(content), FetchedAtUtc = Imported,
                    CreatedDate = Imported,
                });
                await db.SaveChangesAsync();
                ids.Add(recipe.Id);
            }
            db.ChangeTracker.Clear();

            var nutrition = new RecipeNutritionService(db, NullLogger<RecipeNutritionService>.Instance);
            foreach (var id in ids) await nutrition.RecalculateAsync(id);
            db.ChangeTracker.Clear();

            var rebuilt = new List<long>();
            foreach (var id in ids.Where(_ => rng.Next(0, 12) == 0).ToList())
            {
                await CraftRebuildAsync(db, rng, id, foods);
                rebuilt.Add(id);
            }
            foreach (var id in ids.Where(id => !rebuilt.Contains(id) && rng.Next(0, 30) == 0).ToList()) await CraftRuleRejectAsync(db, id);

            var passStarted = DateTime.UtcNow;
            var service = Service(db);
            foreach (var chunk in ids.Chunk(40))
            {
                await service.RebuildAsync(new RecipeReprocessRequestModel { RecipeIds = chunk.ToList() }, apply: true, orderOnly: true);
                db.ChangeTracker.Clear();
            }

            foreach (var id in ids)
            {
                var roll = rng.Next(0, 100);
                if (roll < 12) await RelinkAsync(db, rng, id, foods, toFoodInRecipe: false);
                else if (roll < 16) await RelinkAsync(db, rng, id, foods, toFoodInRecipe: true);
                else if (roll < 22) await ChangeAmountAsync(db, rng, id);
                else if (roll < 25) await AddLineAsync(db, id, foods);
                else if (roll < 30) await SetKindAsync(db, id);
                else if (roll < 32) await DeleteLineAsync(db, id);
                else if (roll < 34) await CraftDuplicateRetireAsync(db, id, ids[0]);
                else if (roll < 37 && rebuilt.Contains(id)) await ApproveAsync(db, id);
                else if (roll < 39 && rebuilt.Contains(id)) await ChangeStepAsync(db, id);
                db.ChangeTracker.Clear();
            }

            await FillNutrientsAsync(db, foods);
            return new World { Db = db, PassStarted = passStarted, RecipeIds = ids, Store = store };
        }

        private static async Task FillNutrientsAsync(ApplicationDbContext db, List<IngredientEntity> foods)
        {
            for (var i = 0; i < foods.Count; i++)
            {
                var foodId = foods[i].Id;
                if (i % 3 == 0)
                {
                    db.IngredientNutrients.Add(new IngredientNutrientEntity
                    {
                        IngredientId = foodId, NutrientId = 4, Amount = 1.2m + i / 10m, MeasurementId = Gram, CreatedDate = AfterPass,
                    });
                }
                if (i % 5 == 0)
                {
                    var calories = await db.IngredientNutrients.SingleAsync(n => n.IngredientId == foodId && n.NutrientId == 1);
                    calories.Amount += 25m;
                }
            }
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
        }

        private static async Task CraftRebuildAsync(ApplicationDbContext db, Random rng, long recipeId, List<IngredientEntity> foods)
        {
            var recipe = await db.Recipes.SingleAsync(r => r.Id == recipeId);
            var rows = await RowsAsync(db, recipeId);
            var steps = await StepsAsync(db, recipeId);
            var line = rows[rng.Next(0, rows.Count)];
            var made = new IngredientEntity
            {
                Name = "made " + recipeId.ToString(CultureInfo.InvariantCulture),
                CurationStatusId = (long)CurationStatusEnum.NonCurated,
                CreatedDate = BeforePass,
            };
            db.Ingredients.Add(made);
            await db.SaveChangesAsync();
            if (rng.Next(0, 3) == 0)
            {
                db.IngredientAliases.Add(new IngredientAliasEntity { IngredientId = made.Id, AliasName = "made alias " + recipeId.ToString(CultureInfo.InvariantCulture), CreatedDate = BeforePass });
            }

            var newRows = rows.Select(r => r == line ? r with { IngredientId = made.Id, LastModifiedDate = BeforePass, LastModifiedByPersonId = SystemConstants.SystemPersonId } : r).ToList();
            await ReplaceRowsAsync(db, recipeId, newRows);

            var changeSteps = rng.Next(0, 2) == 0;
            var newSteps = changeSteps
                ? steps.Select(s => s with { Description = s.Description + " Bake it.", LastModifiedDate = BeforePass, LastModifiedByPersonId = SystemConstants.SystemPersonId }).ToList()
                : null;
            if (newSteps != null)
            {
                foreach (var step in await db.RecipeSteps.IgnoreQueryFilters().Where(s => s.RecipeId == recipeId).ToListAsync())
                {
                    step.Description += " Bake it.";
                    step.LastModifiedDate = BeforePass;
                    step.LastModifiedByPersonId = SystemConstants.SystemPersonId;
                }
            }

            var oldState = new RecipeReprocessService.RecipeState(recipe.CurationStatusId, recipe.VettingIssues, recipe.IsDeleted, recipe.DeletedAt, recipe.DeletedByPersonId);
            if (recipe.CurationStatusId == (long)CurationStatusEnum.NonCurated && rng.Next(0, 2) == 0)
            {
                recipe.CurationStatusId = (long)CurationStatusEnum.RequiresRevision;
                recipe.VettingIssues = "Only 1 step";
                recipe.LastModifiedDate = BeforePass;
            }
            var newState = new RecipeReprocessService.RecipeState(recipe.CurationStatusId, recipe.VettingIssues, recipe.IsDeleted, recipe.DeletedAt, recipe.DeletedByPersonId);

            var link = new AuditLogEntryEntity
            {
                EntityType = LinkApplyJournal.EntityType, EntityId = 0, ChangeType = LinkApplyJournal.ChangeType, PropertyName = LinkApplyJournal.Line,
                OldValue = "crafted", NewValue = "crafted", Timestamp = BeforePass, ChangedByPersonId = Operator,
            };
            db.AuditLogEntries.Add(link);
            var journal = new RecipeReprocessJournalEntity
            {
                RecipeId = recipeId, BatchId = CraftedBatch, Kind = RecipeReprocessKind.Rebuild, RuleVersion = RecipeLineBuilder.Version,
                OldRows = JsonSerializer.Serialize(rows, Json), NewRows = JsonSerializer.Serialize(newRows, Json),
                OldSteps = newSteps == null ? null : JsonSerializer.Serialize(steps, Json), NewSteps = newSteps == null ? null : JsonSerializer.Serialize(newSteps, Json),
                OldRecipe = JsonSerializer.Serialize(oldState, Json), NewRecipe = JsonSerializer.Serialize(newState, Json),
                CreatedDate = BeforePass, CreatedByPersonId = Operator,
            };
            db.RecipeReprocessJournals.Add(journal);
            await db.SaveChangesAsync();
            db.AuditLogEntries.Add(new AuditLogEntryEntity
            {
                EntityType = ScrapedRecipeRepairService.LineEntityType, EntityId = recipeId, ChangeType = RecipeReprocessService.ReprocessChangeType, PropertyName = journal.Kind,
                NewValue = string.Create(CultureInfo.InvariantCulture, $"journal={journal.Id};batch={CraftedBatch};rules={journal.RuleVersion};kind={journal.Kind};links={link.Id}"),
                Timestamp = BeforePass, ChangedByPersonId = Operator,
            });
            db.AuditLogEntries.Add(new AuditLogEntryEntity
            {
                EntityType = RecipeReprocessService.FoodEntityType, EntityId = made.Id, ChangeType = RecipeReprocessService.FoodCreateChangeType, PropertyName = nameof(IngredientEntity.Name),
                OldValue = string.Create(CultureInfo.InvariantCulture, $"journal={journal.Id};batch={CraftedBatch}"), NewValue = made.Name,
                Timestamp = BeforePass, ChangedByPersonId = Operator,
            });
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            await new RecipeNutritionService(db, NullLogger<RecipeNutritionService>.Instance).RecalculateAsync(recipeId);
            db.ChangeTracker.Clear();
        }

        private static async Task CraftRuleRejectAsync(ApplicationDbContext db, long recipeId)
        {
            var recipe = await db.Recipes.SingleAsync(r => r.Id == recipeId);
            var oldState = new RecipeReprocessService.RecipeState(recipe.CurationStatusId, recipe.VettingIssues, recipe.IsDeleted, recipe.DeletedAt, recipe.DeletedByPersonId);
            recipe.CurationStatusId = (long)CurationStatusEnum.Rejected;
            recipe.VettingIssues = RecipeReprocessService.RuleRejectIssue;
            recipe.LastModifiedDate = BeforePass;
            var newState = new RecipeReprocessService.RecipeState(recipe.CurationStatusId, recipe.VettingIssues, recipe.IsDeleted, recipe.DeletedAt, recipe.DeletedByPersonId);
            var journal = new RecipeReprocessJournalEntity
            {
                RecipeId = recipeId, BatchId = CraftedBatch, Kind = RecipeReprocessKind.RuleReject, RuleVersion = RecipeLineBuilder.Version,
                OldRecipe = JsonSerializer.Serialize(oldState, Json), NewRecipe = JsonSerializer.Serialize(newState, Json),
                CreatedDate = BeforePass, CreatedByPersonId = Operator,
            };
            db.RecipeReprocessJournals.Add(journal);
            await db.SaveChangesAsync();
            db.AuditLogEntries.Add(new AuditLogEntryEntity
            {
                EntityType = RecipeReprocessService.RecipeEntityType, EntityId = recipeId, ChangeType = RecipeReprocessService.RuleRejectChangeType, PropertyName = journal.Kind,
                OldValue = journal.OldRecipe, NewValue = string.Create(CultureInfo.InvariantCulture, $"journal={journal.Id};batch={CraftedBatch}"),
                Timestamp = BeforePass, ChangedByPersonId = Operator,
            });
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
        }

        private static async Task CraftDuplicateRetireAsync(ApplicationDbContext db, long recipeId, long keptId)
        {
            if (recipeId == keptId) return;
            var recipe = await db.Recipes.IgnoreQueryFilters().SingleAsync(r => r.Id == recipeId);
            if (recipe.IsDeleted) return;
            var oldState = new RecipeReprocessService.RecipeState(recipe.CurationStatusId, recipe.VettingIssues, recipe.IsDeleted, recipe.DeletedAt, recipe.DeletedByPersonId);
            recipe.IsDeleted = true;
            recipe.DeletedAt = AfterPass;
            recipe.DeletedByPersonId = SystemConstants.SystemPersonId;
            var newState = new RecipeReprocessService.RecipeState(recipe.CurationStatusId, recipe.VettingIssues, recipe.IsDeleted, recipe.DeletedAt, recipe.DeletedByPersonId);
            var journal = new RecipeReprocessJournalEntity
            {
                RecipeId = recipeId, BatchId = "rp-crafted-duplicates", Kind = RecipeReprocessKind.DuplicateRetire, RuleVersion = RecipeLineBuilder.Version,
                OldRecipe = JsonSerializer.Serialize(oldState, Json), NewRecipe = JsonSerializer.Serialize(newState, Json), KeptRecipeId = keptId,
                CreatedDate = AfterPass, CreatedByPersonId = Operator,
            };
            db.RecipeReprocessJournals.Add(journal);
            await db.SaveChangesAsync();
        }

        private static async Task RelinkAsync(ApplicationDbContext db, Random rng, long recipeId, List<IngredientEntity> foods, bool toFoodInRecipe)
        {
            var rows = (await RowsAsync(db, recipeId)).Where(r => !r.IsDeleted).ToList();
            if (rows.Count < 2) return;
            var line = rows[rng.Next(0, rows.Count)];
            long target;
            if (toFoodInRecipe)
            {
                var other = rows.FirstOrDefault(r => r.IngredientId != line.IngredientId && rows.All(o => o.IngredientId != r.IngredientId || o.SortOrder != line.SortOrder));
                if (other == null) return;
                target = other.IngredientId;
            }
            else
            {
                var free = foods.Where(f => rows.All(r => r.IngredientId != f.Id)).ToList();
                target = free[rng.Next(0, free.Count)].Id;
            }
            var moved = rows.Select(r => r == line ? r with { IngredientId = target, LastModifiedDate = AfterPass, LastModifiedByPersonId = SystemConstants.SystemPersonId } : r).ToList();
            await ReplaceRowsAsync(db, recipeId, moved.Concat((await RowsAsync(db, recipeId)).Where(r => r.IsDeleted)).ToList());
        }

        private static async Task ChangeAmountAsync(ApplicationDbContext db, Random rng, long recipeId)
        {
            var row = await db.RecipeIngredients.Where(ri => ri.RecipeId == recipeId).OrderBy(ri => ri.SortOrder).FirstAsync();
            row.Quantity += rng.Next(1, 4);
            row.LastModifiedDate = AfterPass;
            await db.SaveChangesAsync();
        }

        private static async Task AddLineAsync(ApplicationDbContext db, long recipeId, List<IngredientEntity> foods)
        {
            var rows = await RowsAsync(db, recipeId);
            var food = foods.First(f => rows.All(r => r.IngredientId != f.Id));
            db.RecipeIngredients.Add(new RecipeIngredientEntity
            {
                RecipeId = recipeId, IngredientId = food.Id, SortOrder = rows.Max(r => r.SortOrder) + 1, Quantity = 1, MeasurementId = Piece,
                RawLine = "1 " + food.Name + " added by hand", CreatedDate = AfterPass, CreatedByPersonId = Operator,
            });
            await db.SaveChangesAsync();
        }

        private static async Task SetKindAsync(ApplicationDbContext db, long recipeId)
        {
            var row = await db.RecipeIngredients.Where(ri => ri.RecipeId == recipeId && ri.LineKind == null).OrderBy(ri => ri.SortOrder).FirstOrDefaultAsync();
            if (row == null) return;
            row.LineKind = "ingredient";
            await db.SaveChangesAsync();
        }

        private static async Task DeleteLineAsync(ApplicationDbContext db, long recipeId)
        {
            var row = await db.RecipeIngredients.Where(ri => ri.RecipeId == recipeId).OrderByDescending(ri => ri.SortOrder).FirstAsync();
            row.IsDeleted = true;
            row.DeletedAt = AfterPass;
            row.DeletedByPersonId = Operator;
            await db.SaveChangesAsync();
        }

        private static async Task ApproveAsync(ApplicationDbContext db, long recipeId)
        {
            var recipe = await db.Recipes.IgnoreQueryFilters().SingleAsync(r => r.Id == recipeId);
            recipe.CurationStatusId = (long)CurationStatusEnum.Curated;
            recipe.VettingIssues = null;
            recipe.LastModifiedDate = AfterPass;
            await db.SaveChangesAsync();
        }

        private static async Task ChangeStepAsync(ApplicationDbContext db, long recipeId)
        {
            var step = await db.RecipeSteps.IgnoreQueryFilters().Where(s => s.RecipeId == recipeId).OrderBy(s => s.StepNumber).FirstAsync();
            step.Description += " Serve warm.";
            step.LastModifiedDate = AfterPass;
            await db.SaveChangesAsync();
        }

        private static Task<List<RecipeReprocessService.LineRow>> RowsAsync(ApplicationDbContext db, long recipeId) =>
            db.RecipeIngredients.IgnoreQueryFilters().AsNoTracking()
                .Where(ri => ri.RecipeId == recipeId)
                .OrderBy(ri => ri.SortOrder).ThenBy(ri => ri.IngredientId)
                .Select(ri => new RecipeReprocessService.LineRow(
                    ri.IngredientId, ri.SortOrder, ri.Quantity, ri.MeasurementId, ri.RawLine, ri.LineKind, EF.Property<long?>(ri, "IngredientEntityId"),
                    ri.CreatedDate, ri.CreatedByPersonId, ri.LastModifiedDate, ri.LastModifiedByPersonId, ri.IsDeleted, ri.DeletedAt, ri.DeletedByPersonId,
                    ri.AlcoholRetention, ri.AlcoholRetentionBasis))
                .ToListAsync();

        private static Task<List<RecipeReprocessService.StepRow>> StepsAsync(ApplicationDbContext db, long recipeId) =>
            db.RecipeSteps.IgnoreQueryFilters().AsNoTracking()
                .Where(s => s.RecipeId == recipeId)
                .OrderBy(s => s.StepNumber)
                .Select(s => new RecipeReprocessService.StepRow(s.StepNumber, s.StepTypeId, s.Summary, s.Description, s.Id, s.CreatedDate, s.CreatedByPersonId,
                    s.LastModifiedDate, s.LastModifiedByPersonId, s.IsDeleted, s.DeletedAt, s.DeletedByPersonId))
                .ToListAsync();

        private static async Task ReplaceRowsAsync(ApplicationDbContext db, long recipeId, List<RecipeReprocessService.LineRow> rows)
        {
            db.RecipeIngredients.RemoveRange(await db.RecipeIngredients.IgnoreQueryFilters().Where(ri => ri.RecipeId == recipeId).ToListAsync());
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            foreach (var r in rows)
            {
                var entity = new RecipeIngredientEntity
                {
                    RecipeId = recipeId, IngredientId = r.IngredientId, SortOrder = r.SortOrder, Quantity = r.Quantity, MeasurementId = r.MeasurementId,
                    RawLine = r.RawLine, LineKind = r.LineKind, CreatedDate = r.CreatedDate, CreatedByPersonId = r.CreatedByPersonId,
                    LastModifiedDate = r.LastModifiedDate, LastModifiedByPersonId = r.LastModifiedByPersonId, IsDeleted = r.IsDeleted,
                    DeletedAt = r.DeletedAt, DeletedByPersonId = r.DeletedByPersonId, AlcoholRetention = r.AlcoholRetention, AlcoholRetentionBasis = r.AlcoholRetentionBasis,
                };
                db.RecipeIngredients.Add(entity);
                db.Entry(entity).Property("IngredientEntityId").CurrentValue = r.IngredientEntityId;
            }
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
        }

        /// <summary>
        /// Every row the undo can touch, as text: lines, steps, recipe states, foods, journals (with their rows
        /// before and after), the audit rows other than an undo run's own progress rows, and the nutrition
        /// labels' values. A time from before the pass is printed as it is; one from the pass prints
        /// <c>pass</c> and one from the undo <c>undo</c>. Batch ids are numbered by their first journal.
        /// </summary>
        public static async Task<string> DumpAsync(ApplicationDbContext db, DateTime passStarted, DateTime undoStarted)
        {
            db.ChangeTracker.Clear();
            string T(DateTime? t) => t == null ? "-" : t < passStarted ? t.Value.ToString("O", CultureInfo.InvariantCulture) : t < undoStarted ? "pass" : "undo";
            var journals = await db.RecipeReprocessJournals.IgnoreQueryFilters().AsNoTracking().OrderBy(j => j.Id).ToListAsync();
            var batches = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var j in journals)
            {
                if (!batches.ContainsKey(j.BatchId) && !j.BatchId.StartsWith(RecipeReprocessService.OrderFollowPrefix, StringComparison.Ordinal))
                    batches[j.BatchId] = "batch" + (batches.Count + 1).ToString(CultureInfo.InvariantCulture);
            }
            string B(string? text)
            {
                if (text == null) return "-";
                foreach (var (id, label) in batches.OrderByDescending(b => b.Key.Length)) text = text.Replace(id, label, StringComparison.Ordinal);
                return text;
            }
            string Rows(string? json) => json == null ? "-" : Hash(string.Join("|", (JsonSerializer.Deserialize<List<RecipeReprocessService.LineRow>>(json, Json) ?? new())
                .Select(r => string.Join(",", r.IngredientId, r.SortOrder, r.Quantity.ToString(CultureInfo.InvariantCulture), r.MeasurementId, r.RawLine, r.LineKind ?? "-", r.IngredientEntityId?.ToString(CultureInfo.InvariantCulture) ?? "-",
                    T(r.CreatedDate), r.CreatedByPersonId, T(r.LastModifiedDate), r.LastModifiedByPersonId, r.IsDeleted, T(r.DeletedAt), r.DeletedByPersonId, r.AlcoholRetention, r.AlcoholRetentionBasis))));
            string Steps(string? json) => json == null ? "-" : Hash(string.Join("|", (JsonSerializer.Deserialize<List<RecipeReprocessService.StepRow>>(json, Json) ?? new())
                .Select(s => string.Join(",", s.StepNumber, s.StepTypeId, s.Summary, s.Description, s.Id, T(s.CreatedDate), s.CreatedByPersonId, T(s.LastModifiedDate), s.LastModifiedByPersonId, s.IsDeleted, T(s.DeletedAt), s.DeletedByPersonId))));

            var lines = new List<string>();
            foreach (var r in (await db.RecipeIngredients.IgnoreQueryFilters().AsNoTracking()
                         .Select(ri => new { ri, EntityId = EF.Property<long?>(ri, "IngredientEntityId") }).ToListAsync())
                     .OrderBy(x => x.ri.RecipeId).ThenBy(x => x.ri.SortOrder).ThenBy(x => x.ri.IngredientId))
            {
                var ri = r.ri;
                lines.Add(string.Join("\t", "line", ri.RecipeId, ri.IngredientId, ri.SortOrder, ri.Quantity.ToString(CultureInfo.InvariantCulture), ri.MeasurementId, ri.RawLine, ri.LineKind ?? "-",
                    r.EntityId?.ToString(CultureInfo.InvariantCulture) ?? "-", T(ri.CreatedDate), ri.CreatedByPersonId, T(ri.LastModifiedDate), ri.LastModifiedByPersonId, ri.IsDeleted, T(ri.DeletedAt), ri.DeletedByPersonId,
                    ri.AlcoholRetention, ri.AlcoholRetentionBasis));
            }
            foreach (var s in (await db.RecipeSteps.IgnoreQueryFilters().AsNoTracking().ToListAsync()).OrderBy(s => s.RecipeId).ThenBy(s => s.StepNumber))
            {
                lines.Add(string.Join("\t", "step", s.RecipeId, s.StepNumber, s.Id, s.Summary, s.Description, T(s.CreatedDate), T(s.LastModifiedDate), s.LastModifiedByPersonId, s.IsDeleted));
            }
            foreach (var r in (await db.Recipes.IgnoreQueryFilters().AsNoTracking().ToListAsync()).OrderBy(r => r.Id))
            {
                lines.Add(string.Join("\t", "recipe", r.Id, r.CurationStatusId, r.VettingIssues ?? "-", r.IsDeleted, T(r.DeletedAt), r.DeletedByPersonId, T(r.LastModifiedDate), r.LastModifiedByPersonId));
            }
            foreach (var f in (await db.Ingredients.IgnoreQueryFilters().AsNoTracking().ToListAsync()).OrderBy(f => f.Id))
            {
                lines.Add(string.Join("\t", "food", f.Id, f.Name, f.CurationStatusId, f.IsDeleted));
            }
            foreach (var j in journals)
            {
                lines.Add(string.Join("\t", "journal", j.Id, j.RecipeId, B(j.BatchId), j.Kind, T(j.UndoneAt), T(j.CreatedDate), j.CreatedByPersonId, j.SourceDocumentId, j.KeptRecipeId,
                    Rows(j.OldRows), Rows(j.NewRows), Steps(j.OldSteps), Steps(j.NewSteps), j.OldRecipe ?? "-", j.NewRecipe ?? "-"));
            }
            foreach (var a in (await db.AuditLogEntries.AsNoTracking().Where(a => a.EntityType != RunEntityType).ToListAsync())
                     .Select(a => string.Join("\t", "audit", a.EntityType, a.EntityId, a.ChangeType, a.PropertyName ?? "-", B(a.OldValue), B(a.NewValue), T(a.Timestamp), a.ChangedByPersonId))
                     .OrderBy(s => s, StringComparer.Ordinal))
            {
                lines.Add(a);
            }
            foreach (var n in (await db.RecipeNutrition.IgnoreQueryFilters().AsNoTracking().ToListAsync()).OrderBy(n => n.RecipeId).ThenBy(n => n.NutrientId))
            {
                lines.Add(string.Join("\t", "nutrition", n.RecipeId, n.NutrientId, n.Amount.ToString(CultureInfo.InvariantCulture), n.Unit, n.IsDeleted));
            }
            return string.Join("\n", lines) + "\n";
        }

        public static string Counts(RecipeReprocessUndoResult r) =>
            string.Join("\t", "counts", r.Entries, r.Restored, r.Skipped, r.StateKept, r.PositionsFollowed, r.FoodsRemoved, r.FoodsKept,
                string.Join(",", r.RestoredByKind.OrderBy(k => k.Key, StringComparer.Ordinal).Select(k => k.Key + "=" + k.Value.ToString(CultureInfo.InvariantCulture))),
                string.Join(",", r.SkippedRecipeIds));

        private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16].ToLowerInvariant();
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Nom.Data;
using Nom.Data.Audit;
using Nom.Data.Measurement;
using Nom.Data.Nutrient;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Curation;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;
using Nom.Orch.Settings;
using Nom.Orch.UtilityInterfaces;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// Staging checkpoint 9, U18: once the removal had purged a forbidden source's recipes, the alcohol retention undo failed
    /// with HTTP 500 on the purge's marker in eight journal values. Every undo handle of the data order, walked in the order the
    /// checkpoint ran them (newest first), runs as a dry run and as an apply after a purge that rewrote the journal values of a
    /// recipe each step wrote, fails on none, and never puts a purged recipe back; the retention undo names what it skips and
    /// still takes back the recipe that was not removed.
    /// </summary>
    [Collection(RecipeRemovalTests.Gate)]
    public class UndoAfterPurgeTests
    {
        private const long Admin = 7, Gram = 1, Kcal = 16, Calories = 5035, Alcohol = 5038;
        private const string Batch = "w01";

        private sealed class AdminUser : ICurrentUserService
        {
            public long? PersonId => Admin;
            public long PersonIdOrSystem => Admin;
            public long RequiredPersonId => Admin;
            public string? UserId => "admin";
            public string RequiredUserId => "admin";
        }

        /// <summary>The undo handles of staging checkpoint 9's step 6, newest first, that read a recipe's journal.</summary>
        public static readonly string[] Handles =
        {
            "startup-fills/undo",
            "ProseRewrite/recheck/undo",
            "rights/removal/undo",
            "rights/undo?step=credits",
            "rights/undo?step=titles",
            "rights/undo?step=facts-only",
            "rights/undo?step=retention",
            "ProseRewrite/stock/restore",
            "alcohol-retention/undo",
            "FoodCatalog/relink/undo",
            "FoodCatalog/wordings/unload?withLines=true",
            "link-recheck/undo",
            "RecipeReprocess/undo?kind=order",
            "RecipeReprocess/undo?kind=rebuild",
            "RecipeSource/attach/undo",
        };

        public static IEnumerable<object[]> HandleData => Handles.Select(h => new object[] { h });

        private sealed record World(long Stew, long Open, long Wine, long SweetWine, DateTime Since, DateTime RetentionStamp, DateTime RightsStamp);

        private static ApplicationDbContext NewContext() =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>()
                    .UseInMemoryDatabase(Guid.NewGuid().ToString())
                    .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                    .Options,
                new HttpContextAccessor { HttpContext = new DefaultHttpContext() });

        private static AuditLogEntryEntity Entry(string entityType, long entityId, string changeType, string property, string? oldValue, string? newValue, DateTime at) => new()
        {
            EntityType = entityType, EntityId = entityId, ChangeType = changeType, PropertyName = property, OldValue = oldValue, NewValue = newValue, Timestamp = at, ChangedByPersonId = Admin,
        };

        private static async Task<World> SeedAsync(ApplicationDbContext db)
        {
            var since = DateTime.UtcNow.AddSeconds(-5);
            db.Persons.Add(new Nom.Data.Person.PersonEntity { Id = Admin, Name = "Admin", UserId = "admin" });
            db.Set<BaseMeasurementEntity>().AddRange(
                new BaseMeasurementEntity { Id = Gram, Name = "Gram", Symbol = "g", MeasurementCategoryId = 1, IsBaseUnit = true, BaseUnitConversionFactor = 1m },
                new BaseMeasurementEntity { Id = Kcal, Name = "Kilocalorie", Symbol = "kcal", MeasurementCategoryId = 5, BaseUnitConversionFactor = 1m });
            db.Nutrients.AddRange(
                new NutrientEntity { Id = Calories, Name = "Calories", DefaultMeasurementId = Kcal },
                new NutrientEntity { Id = Alcohol, Name = "Alcohol", DefaultMeasurementId = Gram });
            var wine = new IngredientEntity { Name = "Dry White Wine", FdcId = "174838", FdcDataType = "sr_legacy_food", CurationStatusId = (long)CurationStatusEnum.Curated, FdcCategoryId = 14 };
            wine.IngredientNutrients.Add(new IngredientNutrientEntity { NutrientId = Calories, Amount = 82m, MeasurementId = Kcal });
            wine.IngredientNutrients.Add(new IngredientNutrientEntity { NutrientId = Alcohol, Amount = 10.3m, MeasurementId = Gram });
            var sweet = new IngredientEntity { Name = "sweet wine", CurationStatusId = (long)CurationStatusEnum.NonCurated };
            db.Ingredients.AddRange(wine, sweet);
            db.ScrapingSources.Add(new ScrapingSourceEntity
            {
                Domain = "forbidden.example", Status = ScrapingSourceStatusEnum.Approved, Kind = ScrapingSourceEntity.WebKind, CreatedDate = since, RightsClass = SourceRightsRule.Forbidden,
                RightsQuote = "You agree not to use any robot or spider.",
            });
            await db.SaveChangesAsync();

            RecipeEntity Recipe(string url, string name) => new()
            {
                AuthorId = Admin, Name = name, SourceUrl = url, SourceSite = new Uri(url).Host, ScrapedAtUtc = since.AddDays(-60), SourceCredit = "Adapted from the site",
                CurationStatusId = (long)CurationStatusEnum.Curated, Visibility = RecipeVisibilityEnum.Public, Servings = 4,
                RecipeSteps = new List<RecipeStepEntity> { new() { StepNumber = 1, Summary = "Cook", Description = "Add the wine and simmer 30 minutes." } },
                RecipeIngredients = new List<RecipeIngredientEntity>
                {
                    new() { IngredientId = wine.Id, Quantity = 118m, MeasurementId = Gram, RawLine = "1/2 cup dry white wine", SortOrder = 0 },
                    new() { IngredientId = sweet.Id, Quantity = 0m, MeasurementId = Gram, RawLine = "1 cup sweet wine", SortOrder = 1 },
                },
            };
            var stew = Recipe("https://forbidden.example/stew/", "Wine Stew");
            var open = Recipe("https://open.example/stew/", "Open Wine Stew");
            db.Recipes.AddRange(stew, open);
            await db.SaveChangesAsync();
            foreach (var r in new[] { stew, open })
                db.ScrapedDocuments.Add(new ScrapedDocumentEntity { RecipeId = r.Id, SourceUrl = r.SourceUrl!, RawJsonLd = "{\"name\":\"page\"}", Format = ScrapedDocumentFormat.JsonLd, FetchedAtUtc = since.AddDays(-60) });
            await db.SaveChangesAsync();

            var user = new AdminUser();
            (await new VerifiedWordingService(db, user).LoadAsync(new[]
            {
                new VerifiedWordingRow { Row = "1", Wording = "sweet wine", Verdict = IngredientWordingVerdict.Food, FoodId = wine.Id, FoodName = wine.Name },
            }, Batch, apply: true)).Rejected.Should().Be(0);
            (await new LineRelinkService(db, user).LinkVerifiedWordingsAsync(apply: true, batch: Batch)).Moved.Should().Be(2);
            db.ChangeTracker.Clear();
            var retention = await Retention(db).RunAsync(apply: true, maxRecipes: 1000);
            retention.Changed.Should().BeGreaterThan(0);
            db.ChangeTracker.Clear();

            var rights = DateTime.UtcNow;
            foreach (var r in new[] { stew, open })
            {
                db.AuditLogEntries.AddRange(
                    Entry(ScrapedRecipeRepairService.LineEntityType, r.Id, ScrapedRecipeRepairService.FillChangeType, RecipeLineClassificationService.LinePropertyName(wine.Id, 0),
                        PackageLineService.Amount(wine.Id, 0, 0m, Gram), PackageLineService.Amount(wine.Id, 0, 118m, Gram), rights),
                    Entry(ProseRecheckService.RecipeEntityType, r.Id, ProseRecheckService.RepairChangeType, nameof(RecipeEntity.ProseWithheld), "2", "3", rights),
                    Entry(ProseRecheckService.RecipeEntityType, r.Id, SourceRightsService.TitleChangeType, nameof(RecipeEntity.Name), r.Name + " (old)", r.Name, rights),
                    Entry(ProseRecheckService.RecipeEntityType, r.Id, SourceRightsService.CreditChangeType, nameof(RecipeEntity.SourceCredit), "Recipe by the site", r.SourceCredit, rights),
                    Entry(ProseRecheckService.RecipeEntityType, r.Id, SourceRightsService.FactsOnlyWithholdChangeType, nameof(RecipeEntity.ProseWithheld), "false", "true", rights),
                    Entry(ProseRecheckService.RecipeEntityType, r.Id, SourceRightsService.RetentionChangeType, "Documents", "{\"removed\":[]}", "{}", rights));
                var document = await db.ScrapedDocuments.AsNoTracking().FirstAsync(d => d.RecipeId == r.Id);
                db.AuditLogEntries.Add(Entry(RecipeSourceService.DocumentEntityType, document.Id, RecipeSourceService.AttachChangeType, "RecipeId", null, r.Id.ToString(), rights));
            }
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var removal = new RecipeRemovalService(db, NullLogger<RecipeRemovalService>.Instance);
            (await removal.RetireAsync(null, apply: true, Admin, "owner's decision")).Recipes.Should().Be(1);
            db.ChangeTracker.Clear();
            (await removal.PurgeAsync(null, apply: true, backupTaken: true, Admin)).Recipes.Should().Be(1);
            db.ChangeTracker.Clear();
            (await db.AuditLogEntries.CountAsync(a => a.EntityId == stew.Id && a.ChangeType == AlcoholRetentionService.ChangeType && a.NewValue!.StartsWith(RecipeRemovalService.PurgedValuePrefix)))
                .Should().BeGreaterThan(0, "the purge replaced the purged recipe's retention journal values with its marker, as on staging");
            return new World(stew.Id, open.Id, wine.Id, sweet.Id, since, retention.StampUtc, rights);
        }

        private static AlcoholRetentionService Retention(ApplicationDbContext db) =>
            new(db, new RecipeNutritionService(db, NullLogger<RecipeNutritionService>.Instance), NullLogger<AlcoholRetentionService>.Instance);

        private static Task<object> RunAsync(ApplicationDbContext db, string handle, World w, bool apply)
        {
            var user = new AdminUser();
            var rights = new SourceRightsService(db, Options.Create(new SourceRightsSettings()), NullLogger<SourceRightsService>.Instance);
            var reprocess = new RecipeReprocessService(db, user, new Mock<IRecipeScraperClient>().Object, new RecipeVettingService(), new Mock<IRecipeNutritionService>().Object,
                NullLogger<RecipeReprocessService>.Instance);
            async Task<object> Boxed<T>(Task<T> task) => (await task)!;
            return handle switch
            {
                "startup-fills/undo" => Boxed(new ScrapedRecipeRepairService(db, new RecipeVettingService()).UndoFillsAsync(w.Since, apply)),
                "ProseRewrite/recheck/undo" => Boxed(new ProseRecheckService(db, user, new ProseStockService(db, user)).UndoAsync(w.Since, apply)),
                "rights/removal/undo" => Boxed(new RecipeRemovalService(db, NullLogger<RecipeRemovalService>.Instance).UndoRetireAsync(RetireStamp(db), apply, Admin)),
                "rights/undo?step=credits" => Boxed(rights.UndoStepAsync(SourceRightsService.CreditsStep, w.RightsStamp, apply)),
                "rights/undo?step=titles" => Boxed(rights.UndoStepAsync(SourceRightsService.TitlesStep, w.RightsStamp, apply)),
                "rights/undo?step=facts-only" => Boxed(rights.UndoStepAsync(SourceRightsService.FactsOnlyStep, w.RightsStamp, apply)),
                "rights/undo?step=retention" => Boxed(rights.UndoStepAsync(SourceRightsService.RetentionStep, w.RightsStamp, apply)),
                "ProseRewrite/stock/restore" => Boxed(new ProseStockService(db, user).RestoreAsync(0, apply)),
                "alcohol-retention/undo" => Boxed(Retention(db).UndoAsync(w.RetentionStamp.AddSeconds(-1), apply)),
                "FoodCatalog/relink/undo" => Boxed(new LineRelinkService(db, user).UndoAsync(w.Since, apply)),
                "FoodCatalog/wordings/unload?withLines=true" => Boxed(new VerifiedWordingService(db, user).UnloadAsync(Batch, apply, withLines: true)),
                "link-recheck/undo" => Boxed(new LinkRecheckService(db, user, new Mock<ILinkAutoApplyService>().Object, new LineRelinkService(db, user), new Mock<ILaneFrameService>().Object,
                    NullLogger<LinkRecheckService>.Instance).UndoAsync(w.Since, apply)),
                "RecipeReprocess/undo?kind=order" => Boxed(reprocess.UndoAsync(w.Since, null, RecipeReprocessKind.Order, apply)),
                "RecipeReprocess/undo?kind=rebuild" => Boxed(reprocess.UndoAsync(w.Since, null, RecipeReprocessKind.Rebuild, apply)),
                "RecipeSource/attach/undo" => Boxed(new RecipeSourceService(db, user).UndoAsync(w.Since, apply)),
                _ => throw new ArgumentOutOfRangeException(nameof(handle), handle, "no such undo handle"),
            };
        }

        private static DateTime RetireStamp(ApplicationDbContext db) =>
            db.RecipeRemovalRuns.AsNoTracking().Where(r => r.Kind == RecipeRemovalService.RetireKind).OrderBy(r => r.Id).Select(r => r.Stamp).First();

        [Theory]
        [MemberData(nameof(HandleData))]
        public async Task Every_undo_of_the_data_order_runs_after_a_purge_and_never_puts_a_purged_recipe_back(string handle)
        {
            await using var db = NewContext();
            var w = await SeedAsync(db);
            var purged = await PurgedStateAsync(db, w.Stew);

            var dry = async () => await RunAsync(db, handle, w, apply: false);
            await dry.Should().NotThrowAsync($"the {handle} dry run reads a purged recipe's journal values without failing (staging checkpoint 9, U18)");
            db.ChangeTracker.Clear();
            var apply = async () => await RunAsync(db, handle, w, apply: true);
            await apply.Should().NotThrowAsync($"the {handle} apply skips a purged recipe's entries");
            db.ChangeTracker.Clear();

            (await PurgedStateAsync(db, w.Stew)).Should().Be(purged, "no undo puts anything of a purged recipe back");
        }

        [Fact]
        public async Task The_retention_undo_after_a_purge_names_the_purged_recipes_it_skips_and_takes_back_the_others()
        {
            await using var db = NewContext();
            var w = await SeedAsync(db);

            var dry = await Retention(db).UndoAsync(w.RetentionStamp.AddSeconds(-1));
            var undone = await Retention(db).UndoAsync(w.RetentionStamp.AddSeconds(-1), apply: true);

            (dry.Restored, dry.Skipped).Should().Be((undone.Restored, undone.Skipped));
            undone.Restored.Should().BeGreaterThan(0, "the recipe that was not removed gets its retention back");
            undone.SkippedByReason.Should().ContainKey(AlcoholRetentionService.PurgedSkip);
            undone.PurgedRecipeIds.Should().Equal(w.Stew);
            (await db.RecipeIngredients.AsNoTracking().CountAsync(ri => ri.RecipeId == w.Open && ri.AlcoholRetention != null)).Should().Be(0);
            (await db.AuditLogEntries.CountAsync(a => a.ChangeType == AlcoholRetentionService.ChangeType && a.EntityId == w.Open)).Should().Be(0);
        }

        [Fact]
        public void A_journal_value_another_step_rewrote_is_not_read_as_a_retention()
        {
            AlcoholRetentionService.TryReadJournalValue(RecipeRemovalService.PurgedValuePrefix + "ab12)", out _).Should().BeFalse();
            AlcoholRetentionService.TryReadJournalValue("0.3500|timed:simmer:30", out var read).Should().BeTrue();
            read.Should().Be(((decimal?)0.35m, "timed:simmer:30"));
            AlcoholRetentionService.TryReadJournalValue(string.Empty, out var none).Should().BeTrue();
            none.Should().Be(((decimal?)null, (string?)null));
        }

        private static async Task<string> PurgedStateAsync(ApplicationDbContext db, long recipeId)
        {
            var recipe = await db.Recipes.IgnoreQueryFilters().AsNoTracking().SingleAsync(r => r.Id == recipeId);
            var lines = await db.RecipeIngredients.IgnoreQueryFilters().CountAsync(l => l.RecipeId == recipeId);
            var steps = await db.RecipeSteps.IgnoreQueryFilters().CountAsync(s => s.RecipeId == recipeId);
            var documents = await db.ScrapedDocuments.IgnoreQueryFilters().CountAsync(d => d.RecipeId == recipeId);
            return $"{recipe.IsDeleted}|{recipe.Name}|{recipe.SourceCredit}|{recipe.Description}|{lines}|{steps}|{documents}";
        }
    }
}

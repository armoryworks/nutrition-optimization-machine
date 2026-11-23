using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Nom.Data;
using Nom.Data.Audit;
using Nom.Data.Curation;
using Nom.Data.Nutrient;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Curation;
using Nom.Orch.Models.Recipe;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;
using Nom.Orch.UtilityInterfaces;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// Taking one verified wording back, or one batch, with everything it did: the lines the
    /// relink moved, the ingredients it retired, the proposals and picks it withdrew and the lines
    /// an import landed through it. A fingerprint of lines, ingredients and proposals shows the
    /// round trip: retract or unload gives the state before the load, and loading again gives the
    /// state after it.
    /// </summary>
    public class WordingUndoTests
    {
        private const string Batch = "w01";

        private sealed class NoUser : ICurrentUserService
        {
            public long? PersonId => null;
            public long PersonIdOrSystem => SystemConstants.SystemPersonId;
            public long RequiredPersonId => throw new UnauthorizedAccessException();
            public string? UserId => null;
            public string RequiredUserId => throw new UnauthorizedAccessException();
        }

        private static ApplicationDbContext NewContext() =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options);

        private static VerifiedWordingService Wordings(ApplicationDbContext db) => new(db, new NoUser());

        private static LineRelinkService Relink(ApplicationDbContext db) => new(db, new NoUser());

        private static IngredientEntity CuratedFood(string name) => new()
        {
            Name = name,
            CurationStatusId = (long)CurationStatusEnum.Curated,
            IngredientNutrients = new List<IngredientNutrientEntity> { new() { NutrientId = 1, Amount = 1 } },
        };

        private static IngredientEntity Uncurated(string name) => new() { Name = name, CurationStatusId = (long)CurationStatusEnum.NonCurated };

        private static RecipeEntity Scraped(string name, params (IngredientEntity Food, string Line)[] lines) => new()
        {
            Name = name,
            ScrapedAtUtc = DateTime.UtcNow,
            RecipeIngredients = lines.Select((l, i) => new RecipeIngredientEntity
            {
                IngredientId = l.Food.Id, SortOrder = i, Quantity = 1, MeasurementId = 1, RawLine = l.Line,
            }).ToList(),
        };

        private static FoodCatalogProposalEntity Pick(IngredientEntity ingredient, string wording, IngredientEntity? proposed) => new()
        {
            IngredientId = ingredient.Id, Field = FoodCatalogReviewService.FdcLinkField, Status = FoodProposalStatus.Pending,
            Batch = IngredientLinkService.Batch, Source = "ai:fake:1b", CurrentValue = wording,
            ProposedValue = proposed?.Id.ToString(CultureInfo.InvariantCulture),
        };

        private static VerifiedWordingRow Row(string wording, IngredientEntity food, string? evidence = null) =>
            new() { Row = wording, Wording = wording, Verdict = IngredientWordingVerdict.Food, FoodId = food.Id, FoodName = food.Name, EvidenceKey = evidence };

        private static RecipeScrapingService Importer(ApplicationDbContext db, bool resolve = true)
        {
            var currentUser = new Mock<ICurrentUserService>();
            currentUser.SetupGet(u => u.PersonId).Returns(7);
            var vetting = new Mock<IRecipeVettingService>();
            vetting.Setup(v => v.VetAsync(It.IsAny<ScraperRecipe>())).ReturnsAsync(new List<string>());
            var settings = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { [RecipeScrapingService.ResolveOnImportSetting] = resolve ? "true" : "false" }).Build();
            return new RecipeScrapingService(db, currentUser.Object, new Mock<IRecipeScraperClient>().Object, new Mock<IScrapingSourceService>().Object,
                vetting.Object, NullLogger<RecipeScrapingService>.Instance, settings);
        }

        private static async Task<RecipeEntity> ImportAsync(ApplicationDbContext db, string name, string line, bool resolve = true)
        {
            var recipeName = $"Imported {Guid.NewGuid():N}";
            var result = await Importer(db, resolve).ImportStagedAsync(new StagedImportRequestModel
            {
                Recipes = { new ScraperRecipe { Name = recipeName, Ingredients = { new ScraperIngredient { Name = name, RawLine = line } } } },
            });
            result.Imported.Should().Be(1);
            db.ChangeTracker.Clear();
            return await db.Recipes.SingleAsync(r => r.Name == recipeName);
        }

        /// <summary>Every recipe line, ingredient (live and retired) and link proposal, without timestamps.</summary>
        private static async Task<string> FingerprintAsync(ApplicationDbContext db, long? withoutRecipe = null)
        {
            db.ChangeTracker.Clear();
            var lines = await db.RecipeIngredients.IgnoreQueryFilters().AsNoTracking()
                .Where(ri => ri.RecipeId != withoutRecipe)
                .OrderBy(ri => ri.RecipeId).ThenBy(ri => ri.SortOrder).ThenBy(ri => ri.IngredientId)
                .Select(ri => $"line {ri.RecipeId}:{ri.SortOrder}:{ri.IngredientId}:{ri.Quantity}:{ri.MeasurementId}:{ri.RawLine}:{ri.LineKind}:{ri.IsDeleted}")
                .ToListAsync();
            var foods = await db.Ingredients.IgnoreQueryFilters().AsNoTracking()
                .OrderBy(i => i.Id)
                .Select(i => $"food {i.Id}:{i.Name}:{i.CurationStatusId}:{i.IsDeleted}")
                .ToListAsync();
            var proposals = await db.FoodCatalogProposals.IgnoreQueryFilters().AsNoTracking()
                .OrderBy(p => p.Id)
                .Select(p => $"proposal {p.Id}:{p.IngredientId}:{p.Status}:{p.ProposedValue}:{p.IsDeleted}")
                .ToListAsync();
            return string.Join("\n", lines.Concat(foods).Concat(proposals));
        }

        private sealed record Kitchen(IngredientEntity Extract, IngredientEntity Soymilk, IngredientEntity Vanilla, IngredientEntity Raw,
            RecipeEntity Cake, RecipeEntity Custard, FoodCatalogProposalEntity Open, FoodCatalogProposalEntity WrongPick);

        private static async Task<Kitchen> KitchenAsync(ApplicationDbContext db)
        {
            var extract = CuratedFood("Vanilla Extract");
            var soymilk = CuratedFood("Soymilk");
            var vanilla = Uncurated("vanilla");
            var raw = Uncurated("1 teaspoon vanilla");
            db.Ingredients.AddRange(extract, soymilk, vanilla, raw);
            await db.SaveChangesAsync();
            var cake = Scraped("Cake", (vanilla, "1 tsp. vanilla"), (vanilla, "2 tsp vanilla"));
            var custard = Scraped("Custard", (vanilla, "vanilla"), (raw, "1 teaspoon vanilla"));
            db.Recipes.AddRange(cake, custard);
            var open = Pick(vanilla, "vanilla", null);
            var wrong = Pick(raw, "1 teaspoon vanilla", soymilk);
            db.FoodCatalogProposals.AddRange(open, wrong);
            await db.SaveChangesAsync();
            return new Kitchen(extract, soymilk, vanilla, raw, cake, custard, open, wrong);
        }

        private static async Task<long> LoadAndRelinkAsync(ApplicationDbContext db, string batch, bool overrideRejections = false, params VerifiedWordingRow[] rows)
        {
            var loaded = await Wordings(db).LoadAsync(rows, batch, apply: true, overrideRejections: overrideRejections);
            loaded.Rejected.Should().Be(0);
            await Relink(db).LinkVerifiedWordingsAsync(apply: true, batch: batch);
            db.ChangeTracker.Clear();
            return await db.IngredientWordings.Where(w => w.Wording == rows[0].Wording).Select(w => w.Id).SingleAsync();
        }

        [Fact]
        public async Task An_import_that_lands_through_a_verified_wording_journals_the_line_and_one_by_name_does_not()
        {
            using var db = NewContext();
            var extract = CuratedFood("Vanilla Extract");
            db.Ingredients.Add(extract);
            await db.SaveChangesAsync();
            db.IngredientWordings.Add(new IngredientWordingEntity { Wording = "vanilla", Verdict = IngredientWordingVerdict.Food, IngredientId = extract.Id, Batch = Batch, Source = "verified:w01" });
            await db.SaveChangesAsync();

            var landed = await ImportAsync(db, "Vanilla", "1 tsp Vanilla");
            var byName = await ImportAsync(db, "Vanilla Extract", "1 tsp vanilla extract");
            var off = await ImportAsync(db, "vanilla", "1 tsp vanilla", resolve: false);

            var entry = await db.AuditLogEntries.SingleAsync(a => a.ChangeType == LineRelinkService.WordingResolveChangeType);
            entry.Should().Match<AuditLogEntryEntity>(a => a.EntityType == LineRelinkService.RelinkEntityType && a.EntityId == landed.Id
                && a.OldValue == "vanilla" && a.NewValue == $"{extract.Id}@0" && a.ChangedByPersonId == 7);
            (await db.RecipeIngredients.SingleAsync(ri => ri.RecipeId == byName.Id)).IngredientId.Should().Be(extract.Id);
            (await db.RecipeIngredients.SingleAsync(ri => ri.RecipeId == off.Id)).IngredientId.Should().NotBe(extract.Id);
        }

        [Fact]
        public async Task A_retract_gives_back_the_state_before_the_load_and_loading_it_again_gives_back_the_state_after()
        {
            using var db = NewContext();
            var k = await KitchenAsync(db);
            var before = await FingerprintAsync(db);

            var id = await LoadAndRelinkAsync(db, Batch, false, Row("vanilla", k.Extract));
            var pudding = await ImportAsync(db, "vanilla", "1 tsp vanilla");
            var after = await FingerprintAsync(db);
            after.Should().NotBe(before);
            (await db.RecipeIngredients.CountAsync(ri => ri.IngredientId == k.Extract.Id)).Should().Be(4);

            var retracted = await Wordings(db).RetractAsync(id, "lines are vanilla beans, not extract", apply: true);

            retracted!.Lines.Should().Match<WordingLinesUndoResult>(l => l.Applied && l.SourceIds.SequenceEqual(new[] { k.Vanilla.Id })
                && l.LineMoves == 3 && l.Retirements == 1 && l.ProposalWithdrawals == 1 && l.PickWithdrawals == 1 && l.ImportLandings == 1
                && l.LandingsMovedBack == 1 && l.OnRebuiltRecipes == 0);
            retracted.Lines.Undone!.Should().Match<LineRelinkUndoResult>(u =>
                u.Restored == 3 && u.FoodsRestored == 1 && u.ProposalsRestored == 1 && u.PicksRestored == 1 && u.Skipped == 0);
            (await FingerprintAsync(db, withoutRecipe: pudding.Id)).Should().Be(before);
            (await db.RecipeIngredients.SingleAsync(ri => ri.RecipeId == pudding.Id)).IngredientId.Should().Be(k.Vanilla.Id,
                "the landing goes where the import puts the wording without the table: the ingredient of that name");
            (await db.IngredientWordings.AnyAsync()).Should().BeFalse();
            (await db.IngredientWordingRejections.SingleAsync()).Should().Match<IngredientWordingRejectionEntity>(r =>
                r.Id == retracted.RejectionId && r.Wording == "vanilla" && r.Verdict == IngredientWordingVerdict.Food && r.IngredientId == k.Extract.Id
                && r.Reason == "lines are vanilla beans, not extract" && r.Source == "verified:w01" && r.EvidenceKey == null);
            var left = await db.AuditLogEntries.Select(a => a.ChangeType).ToListAsync();
            left.Should().NotContain(new[]
            {
                LineRelinkService.RelinkChangeType, LineRelinkService.RetireChangeType, LineRelinkService.ProposalWithdrawChangeType,
                LineRelinkService.PickWithdrawChangeType, LineRelinkService.WordingResolveChangeType,
            });
            (await db.AuditLogEntries.Where(a => a.ChangeType == LineRelinkService.WordingRetractChangeType).Select(a => a.EntityType).ToListAsync())
                .Should().BeEquivalentTo(VerifiedWordingService.WordingEntityType, VerifiedWordingService.RejectionEntityType, LineRelinkService.RelinkEntityType);

            var refused = await Wordings(db).LoadAsync(new[] { Row("vanilla", k.Extract) }, "w02", apply: true);
            refused.Problems.Should().ContainSingle().Which.Reason.Should().Be(VerifiedWordingService.RetractedBefore);
            (await db.IngredientWordings.AnyAsync()).Should().BeFalse();

            await LoadAndRelinkAsync(db, Batch, true, Row("vanilla", k.Extract));

            (await FingerprintAsync(db)).Should().Be(after);
            (await db.IngredientWordingRejections.AnyAsync()).Should().BeFalse("loading the pair on purpose lifts the memory");
            (await db.AuditLogEntries.CountAsync(a => a.ChangeType == VerifiedWordingService.RejectionLiftChangeType)).Should().Be(1);
        }

        [Fact]
        public async Task Unloading_a_batch_with_its_lines_gives_back_the_state_before_it_twice_over_and_remembers_nothing()
        {
            using var db = NewContext();
            var k = await KitchenAsync(db);
            var milk = CuratedFood("Milk");
            var sweet = Uncurated("sweet milk");
            db.Ingredients.AddRange(milk, sweet);
            await db.SaveChangesAsync();
            db.Recipes.Add(Scraped("Pudding", (sweet, "1 cup sweet milk")));
            await db.SaveChangesAsync();
            var before = await FingerprintAsync(db);

            await LoadAndRelinkAsync(db, Batch, false, Row("vanilla", k.Extract), Row("sweet milk", milk));
            var after = await FingerprintAsync(db);

            for (var round = 0; round < 2; round++)
            {
                var dry = await Wordings(db).UnloadAsync(Batch, apply: false, withLines: true);
                dry.Lines!.Should().Match<WordingLinesUndoResult>(l => !l.Applied && l.LineMoves == 4 && l.Retirements == 2 && l.Undone == null);
                (await FingerprintAsync(db)).Should().Be(after, "a dry run writes nothing");

                var unloaded = await Wordings(db).UnloadAsync(Batch, apply: true, withLines: true);

                unloaded.Removed.Should().Be(2);
                unloaded.Lines!.Undone!.Should().Match<LineRelinkUndoResult>(u => u.Restored == 4 && u.FoodsRestored == 2 && u.Skipped == 0);
                (await FingerprintAsync(db)).Should().Be(before);
                (await db.IngredientWordingRejections.AnyAsync()).Should().BeFalse("an unload is a rollback, not a verdict");

                await LoadAndRelinkAsync(db, Batch, false, Row("vanilla", k.Extract), Row("sweet milk", milk));
                (await FingerprintAsync(db)).Should().Be(after);
            }
        }

        [Fact]
        public async Task Relinking_one_batch_moves_only_that_batchs_wordings_and_withdraws_only_their_picks()
        {
            using var db = NewContext();
            var k = await KitchenAsync(db);
            var milk = CuratedFood("Milk");
            var sweet = Uncurated("sweet milk");
            db.Ingredients.AddRange(milk, sweet);
            await db.SaveChangesAsync();
            db.Recipes.Add(Scraped("Pudding", (sweet, "1 cup sweet milk")));
            db.FoodCatalogProposals.Add(Pick(sweet, "sweet milk", k.Soymilk));
            await db.SaveChangesAsync();
            await Wordings(db).LoadAsync(new[] { Row("vanilla", k.Extract) }, Batch, apply: true);
            await Wordings(db).LoadAsync(new[] { Row("sweet milk", milk) }, "w02", apply: true);

            var w02 = await Relink(db).LinkVerifiedWordingsAsync(apply: true, batch: "w02");

            w02.Should().Match<VerifiedWordingLinkResult>(r => r.Wordings == 1 && r.Moved == 1 && r.SourcesRetired == 1 && r.PicksWithdrawn == 0 && r.ProposalsWithdrawn == 1);
            (await db.RecipeIngredients.CountAsync(ri => ri.IngredientId == k.Vanilla.Id)).Should().Be(3);
            (await db.FoodCatalogProposals.CountAsync()).Should().Be(2, "the vanilla proposal and pick wait for their own batch");
        }

        [Fact]
        public async Task A_retract_leaves_the_lines_of_a_recipe_rebuilt_after_them_and_says_so()
        {
            using var db = NewContext();
            var k = await KitchenAsync(db);
            var id = await LoadAndRelinkAsync(db, Batch, false, Row("vanilla", k.Extract));
            var pudding = await ImportAsync(db, "vanilla", "1 tsp vanilla");
            foreach (var recipeId in new[] { k.Cake.Id, pudding.Id })
            {
                db.AuditLogEntries.Add(new AuditLogEntryEntity
                {
                    EntityType = ScrapedRecipeRepairService.LineEntityType, EntityId = recipeId, ChangeType = RecipeReprocessService.ReprocessChangeType,
                    PropertyName = "rebuild", NewValue = "journal=1;batch=b;rules=line-builder/2;kind=rebuild", Timestamp = DateTime.UtcNow.AddMinutes(1),
                });
            }
            await db.SaveChangesAsync();

            var dry = await Wordings(db).RetractAsync(id, "wrong food", apply: false);
            var applied = await Wordings(db).RetractAsync(id, "wrong food", apply: true);

            dry!.Lines.OnRebuiltRecipes.Should().Be(3);
            applied!.Lines.Undone!.Should().Match<LineRelinkUndoResult>(u => u.Restored == 1 && u.SkippedRebuilt == 2 && u.FoodsRestored == 1);
            applied.Lines.Should().Match<WordingLinesUndoResult>(l => l.LandingsSkippedRebuilt == 1 && l.LandingsMovedBack == 0);
            (await db.RecipeIngredients.Where(ri => ri.RecipeId == k.Cake.Id || ri.RecipeId == pudding.Id).Select(ri => ri.IngredientId).Distinct().ToListAsync())
                .Should().Equal(k.Extract.Id);
            (await db.RecipeIngredients.SingleAsync(ri => ri.RecipeId == k.Custard.Id && ri.RawLine == "vanilla")).IngredientId.Should().Be(k.Vanilla.Id);
            (await db.AuditLogEntries.CountAsync(a => a.ChangeType == LineRelinkService.RelinkChangeType || a.ChangeType == LineRelinkService.WordingResolveChangeType))
                .Should().Be(3, "the skipped entries stay for after the rebuild is undone");
        }

        [Fact]
        public async Task A_landing_with_no_ingredient_of_its_name_gets_a_new_one_and_a_wording_that_is_its_foods_name_stays()
        {
            using var db = NewContext();
            var extract = CuratedFood("Vanilla Extract");
            db.Ingredients.Add(extract);
            await db.SaveChangesAsync();
            await Wordings(db).LoadAsync(new[] { Row("vanilla", extract), Row("vanilla extract", extract) }, Batch, apply: true);
            var bean = await ImportAsync(db, "vanilla", "1 tsp vanilla");
            var named = await ImportAsync(db, "vanilla extract", "1 tsp vanilla extract");
            var ids = await db.IngredientWordings.OrderBy(w => w.Wording).Select(w => w.Id).ToListAsync();

            var first = await Wordings(db).RetractAsync(ids[0], "beans", apply: true);
            var second = await Wordings(db).RetractAsync(ids[1], "test", apply: true);

            first!.Lines.Should().Match<WordingLinesUndoResult>(l => l.SourceIds.Count == 0 && l.ImportLandings == 1 && l.LandingsMovedBack == 1);
            var moved = await db.RecipeIngredients.SingleAsync(ri => ri.RecipeId == bean.Id);
            (await db.Ingredients.SingleAsync(i => i.Id == moved.IngredientId)).Should().Match<IngredientEntity>(i =>
                i.Name == "vanilla" && i.CurationStatusId == (long)CurationStatusEnum.NonCurated);
            second!.Lines.Should().Match<WordingLinesUndoResult>(l => l.ImportLandings == 1 && l.LandingsKept == 1 && l.LandingsMovedBack == 0);
            (await db.RecipeIngredients.SingleAsync(ri => ri.RecipeId == named.Id)).IngredientId.Should().Be(extract.Id);
            (await db.AuditLogEntries.AnyAsync(a => a.ChangeType == LineRelinkService.WordingResolveChangeType)).Should().BeFalse();
        }

        [Fact]
        public async Task A_dry_run_retract_counts_what_it_would_take_back_and_writes_nothing()
        {
            using var db = NewContext();
            var k = await KitchenAsync(db);
            var id = await LoadAndRelinkAsync(db, Batch, false, Row("vanilla", k.Extract));
            var after = await FingerprintAsync(db);
            var entries = await db.AuditLogEntries.CountAsync();

            var dry = await Wordings(db).RetractAsync(id, "wrong food");

            dry!.Should().Match<WordingRetractResult>(r => !r.Applied && r.RejectionId == null && r.Wording == "vanilla" && r.IngredientId == k.Extract.Id && r.Batch == Batch);
            dry.Lines.Should().Match<WordingLinesUndoResult>(l => l.LineMoves == 3 && l.Retirements == 1 && l.ProposalWithdrawals == 1 && l.PickWithdrawals == 1 && l.Undone == null);
            (await FingerprintAsync(db)).Should().Be(after);
            (await db.AuditLogEntries.CountAsync()).Should().Be(entries);
            (await db.IngredientWordings.CountAsync()).Should().Be(1);
            (await Wordings(db).RetractAsync(id + 100, "none")).Should().BeNull();
        }

        [Fact]
        public async Task An_unclear_row_retracted_opens_its_picks_again_and_is_remembered_with_its_verdict()
        {
            using var db = NewContext();
            var k = await KitchenAsync(db);
            await Wordings(db).LoadAsync(new[] { new VerifiedWordingRow { Row = "1", Wording = "vanilla", Verdict = IngredientWordingVerdict.Unclear } }, Batch, apply: true);
            await Relink(db).LinkVerifiedWordingsAsync(apply: true, batch: Batch);
            (await db.FoodCatalogProposals.CountAsync()).Should().Be(0);
            var id = await db.IngredientWordings.Select(w => w.Id).SingleAsync();

            var retracted = await Wordings(db).RetractAsync(id, "it is one food", apply: true);

            retracted!.Lines.Undone!.PicksRestored.Should().Be(2);
            (await db.FoodCatalogProposals.CountAsync()).Should().Be(2);
            (await db.IngredientWordingRejections.SingleAsync()).Should().Match<IngredientWordingRejectionEntity>(r =>
                r.Verdict == IngredientWordingVerdict.Unclear && r.IngredientId == null);
            var again = await Wordings(db).LoadAsync(new[] { new VerifiedWordingRow { Row = "1", Wording = "vanilla", Verdict = IngredientWordingVerdict.Unclear } }, "w02");
            again.Problems.Single().Reason.Should().Be(VerifiedWordingService.RetractedBefore);
            (await Wordings(db).LoadAsync(new[] { Row("vanilla", k.Extract) }, "w02")).Added.Should().Be(1, "another verdict is not the retracted one");
        }

        [Fact]
        public async Task A_retract_leaves_a_proposal_another_job_withdrew_from_its_source_withdrawn_with_its_entry()
        {
            using var db = NewContext();
            var k = await KitchenAsync(db);
            var admitted = Pick(k.Vanilla, "vanilla", k.Soymilk);
            admitted.IsDeleted = true;
            db.FoodCatalogProposals.Add(admitted);
            await db.SaveChangesAsync();
            db.AuditLogEntries.Add(new AuditLogEntryEntity
            {
                EntityType = LineRelinkService.PickWithdrawEntityType, EntityId = admitted.Id, ChangeType = LineRelinkService.ProposalWithdrawChangeType,
                PropertyName = nameof(FoodCatalogProposalEntity.IsDeleted), OldValue = bool.FalseString, NewValue = bool.TrueString,
                Timestamp = DateTime.UtcNow.AddMinutes(-5), ChangedByPersonId = SystemConstants.SystemPersonId,
            });
            await db.SaveChangesAsync();
            var id = await LoadAndRelinkAsync(db, Batch, false, Row("vanilla", k.Extract));

            var retracted = await Wordings(db).RetractAsync(id, "wrong food", apply: true);

            retracted!.Lines.ProposalWithdrawals.Should().Be(1);
            retracted.Lines.Undone!.ProposalsRestored.Should().Be(1);
            (await db.FoodCatalogProposals.IgnoreQueryFilters().SingleAsync(p => p.Id == admitted.Id)).IsDeleted.Should().BeTrue();
            (await db.FoodCatalogProposals.IgnoreQueryFilters().SingleAsync(p => p.Id == k.Open.Id)).IsDeleted.Should().BeFalse();
            (await db.AuditLogEntries.CountAsync(a => a.EntityId == admitted.Id && a.ChangeType == LineRelinkService.ProposalWithdrawChangeType))
                .Should().Be(1, "the admission's own undo still needs its entry");
        }

        [Fact]
        public async Task Unloading_a_batch_that_replaced_a_row_reopens_only_the_picks_the_restored_row_does_not_contradict()
        {
            using var db = NewContext();
            var k = await KitchenAsync(db);
            await LoadAndRelinkAsync(db, Batch, false, Row("vanilla", k.Extract));
            (await db.FoodCatalogProposals.IgnoreQueryFilters().SingleAsync(p => p.Id == k.WrongPick.Id)).IsDeleted.Should().BeTrue();
            var agreeing = Pick(k.Raw, "vanilla", k.Extract);
            db.FoodCatalogProposals.Add(agreeing);
            await db.SaveChangesAsync();
            (await Wordings(db).LoadAsync(new[] { Row("vanilla", k.Soymilk) }, "w02", apply: true, replace: true)).Changed.Should().Be(1);
            (await Relink(db).LinkVerifiedWordingsAsync(apply: true, batch: "w02")).PicksWithdrawn.Should().Be(1);

            var unloaded = await Wordings(db).UnloadAsync("w02", apply: true, withLines: true);

            unloaded.Restored.Should().Be(1);
            unloaded.Lines!.PickWithdrawals.Should().Be(1);
            (await db.IngredientWordings.SingleAsync()).IngredientId.Should().Be(k.Extract.Id);
            (await db.FoodCatalogProposals.IgnoreQueryFilters().SingleAsync(p => p.Id == k.WrongPick.Id)).IsDeleted.Should().BeTrue(
                "w01 withdrew the pick to Soymilk, and the row is w01's again");
            (await db.FoodCatalogProposals.IgnoreQueryFilters().SingleAsync(p => p.Id == agreeing.Id)).IsDeleted.Should().BeFalse();
        }

        [Fact]
        public async Task A_retract_reload_and_relink_give_back_the_state_before_the_retract_for_a_landing_that_needed_a_new_ingredient()
        {
            using var db = NewContext();
            var extract = CuratedFood("Vanilla Extract");
            db.Ingredients.Add(extract);
            await db.SaveChangesAsync();
            await Wordings(db).LoadAsync(new[] { Row("vanilla", extract) }, Batch, apply: true);
            await ImportAsync(db, "vanilla", "1 tsp vanilla");
            await ImportAsync(db, "vanilla", "a few drops of the good stuff");
            var id = await db.IngredientWordings.Select(w => w.Id).SingleAsync();
            var before = await FingerprintAsync(db);

            var retracted = await Wordings(db).RetractAsync(id, "beans", apply: true);
            retracted!.Lines.LandingsMovedBack.Should().Be(2);
            (await FingerprintAsync(db)).Should().NotBe(before);

            await Wordings(db).LoadAsync(new[] { Row("vanilla", extract) }, "w02", apply: true, overrideRejections: true);
            var dry = await Relink(db).LinkVerifiedWordingsAsync(apply: false, batch: "w02");
            dry.Should().Match<VerifiedWordingLinkResult>(r => r.LandingsReturned == 2 && r.SourcesRetired == 0 && r.IngredientsRemoved == 1);
            (await FingerprintAsync(db)).Should().NotBe(before, "a dry run writes nothing");
            var relinked = await Relink(db).LinkVerifiedWordingsAsync(apply: true, batch: "w02");

            relinked.Should().Match<VerifiedWordingLinkResult>(r => r.LandingsReturned == 2 && r.SourcesRetired == 0 && r.IngredientsRemoved == 1);
            (await FingerprintAsync(db)).Should().Be(before);
            (await db.AuditLogEntries.CountAsync(a => a.ChangeType == LineRelinkService.WordingResolveChangeType)).Should().Be(2,
                "the lines are journaled as landings again, so a later retract finds them");
            (await db.AuditLogEntries.CountAsync(a => a.ChangeType == LineRelinkService.WordingRetractChangeType && a.EntityType != VerifiedWordingService.WordingEntityType
                && a.EntityType != VerifiedWordingService.RejectionEntityType)).Should().Be(0);
        }

        [Fact]
        public async Task A_relink_leaves_a_retracted_landing_on_a_recipe_rebuilt_since_and_keeps_the_ingredient_that_holds_it()
        {
            using var db = NewContext();
            var extract = CuratedFood("Vanilla Extract");
            db.Ingredients.Add(extract);
            await db.SaveChangesAsync();
            await Wordings(db).LoadAsync(new[] { Row("vanilla", extract) }, Batch, apply: true);
            await ImportAsync(db, "vanilla", "1 tsp vanilla");
            var rebuiltRecipe = await ImportAsync(db, "vanilla", "a few drops of the good stuff");
            var id = await db.IngredientWordings.Select(w => w.Id).SingleAsync();
            await Wordings(db).RetractAsync(id, "beans", apply: true);
            var made = (await db.RecipeIngredients.SingleAsync(ri => ri.RecipeId == rebuiltRecipe.Id)).IngredientId;
            db.AuditLogEntries.Add(new AuditLogEntryEntity
            {
                EntityType = ScrapedRecipeRepairService.LineEntityType, EntityId = rebuiltRecipe.Id, ChangeType = RecipeReprocessService.ReprocessChangeType,
                PropertyName = "rebuild", NewValue = "journal=1;batch=b;rules=line-builder/2;kind=rebuild", Timestamp = DateTime.UtcNow.AddMinutes(1),
            });
            await db.SaveChangesAsync();
            await Wordings(db).LoadAsync(new[] { Row("vanilla", extract) }, "w02", apply: true, overrideRejections: true);

            var relinked = await Relink(db).LinkVerifiedWordingsAsync(apply: true, batch: "w02");

            relinked.Should().Match<VerifiedWordingLinkResult>(r => r.LandingsReturned == 1 && r.LandingsReturnSkippedRebuilt == 1 && r.IngredientsRemoved == 0);
            (await db.RecipeIngredients.SingleAsync(ri => ri.RecipeId == rebuiltRecipe.Id)).IngredientId.Should().Be(made);
            (await db.Ingredients.AnyAsync(i => i.Id == made)).Should().BeTrue();
        }

        [Fact]
        public async Task The_rejection_memory_refuses_the_same_pair_on_the_same_evidence_and_lets_another_food_or_new_evidence_through()
        {
            using var db = NewContext();
            var claret = CuratedFood("Alcoholic beverage, wine, table, red, Claret");
            var red = CuratedFood("Red Table Wine");
            var sack = CuratedFood("Dry Dessert Wine");
            db.Ingredients.AddRange(claret, red, sack);
            await db.SaveChangesAsync();
            await Wordings(db).LoadAsync(new[] { Row("claret", claret), Row("sack", sack, "ask:1") }, Batch, apply: true);
            var ids = await db.IngredientWordings.OrderBy(w => w.Wording).Select(w => w.Id).ToListAsync();
            await Wordings(db).RetractAsync(ids[0], "six nutrient rows; claret wine is on red table wine", apply: true);
            await Wordings(db).RetractAsync(ids[1], "sentinel: 2 lines different", apply: true, evidenceKey: "ask:1");

            var check = await Wordings(db).LoadAsync(new[]
            {
                Row("claret", claret, "ask:9"), Row("sack", sack, "ask:1"),
            }, "w02");
            var through = await Wordings(db).LoadAsync(new[] { Row("claret", red), Row("sack", sack, "ask:2") }, "w02", apply: true);

            check.Problems.Select(p => (p.Wording, p.Reason)).Should().BeEquivalentTo(new[]
            {
                ("claret", VerifiedWordingService.RetractedBefore), ("sack", VerifiedWordingService.RetractedBefore),
            });
            through.Should().Match<VerifiedWordingLoadResult>(r => r.Added == 2 && r.Rejected == 0 && r.RejectionsLifted == 0);
            (await db.IngredientWordingRejections.CountAsync()).Should().Be(2, "the memory stays for the pairs it refused");
        }
        [Fact]
        public async Task A_batch_the_system_loaded_under_an_older_key_form_unloads_and_reloads_to_the_same_lines()
        {
            using var db = NewContext();
            var extract = CuratedFood("Vanilla Extract");
            var old = Uncurated("teaspn. vanilla");
            db.Ingredients.AddRange(extract, old);
            await db.SaveChangesAsync();
            db.Recipes.Add(Scraped("Cake", (old, "1 teaspn. vanilla"), (old, "1/2 teaspn. vanilla")));
            await db.SaveChangesAsync();
            var before = await FingerprintAsync(db);
            IngredientNameNormalizer.WordingKey("teaspn. vanilla").Should().NotBe("teaspn. vanilla", "the name normalizer now reads teaspn. as a unit, as the w01 rows teaspn. vanilla and tbp flour found");

            db.IngredientWordings.Add(new IngredientWordingEntity { Wording = "teaspn. vanilla", Verdict = IngredientWordingVerdict.Food, IngredientId = extract.Id, Batch = Batch, Source = "verified:w01" });
            await db.SaveChangesAsync();
            await Relink(db).LinkVerifiedWordingsAsync(apply: true, batch: Batch);
            var after = await FingerprintAsync(db);
            (await db.RecipeIngredients.CountAsync(ri => ri.IngredientId == extract.Id)).Should().Be(2);

            var unloaded = await Wordings(db).UnloadAsync(Batch, apply: true, withLines: true);
            unloaded.Lines!.Undone!.Restored.Should().Be(2);
            (await FingerprintAsync(db)).Should().Be(before);

            (await Wordings(db).LoadAsync(new[] { Row("teaspn. vanilla", extract) }, "w02")).Problems.Should().ContainSingle()
                .Which.Reason.Should().Be(VerifiedWordingService.NotKeyForm, "a batch that never held the wording takes it only in key form");
            var reloaded = await Wordings(db).LoadAsync(new[] { Row("teaspn. vanilla", extract) }, Batch, apply: true);
            reloaded.Rejected.Should().Be(0, "the batch held the wording, so it reloads in the key form it was loaded under");
            await Relink(db).LinkVerifiedWordingsAsync(apply: true, batch: Batch);
            (await FingerprintAsync(db)).Should().Be(after, "retract, unload and reload of a batch the system loaded round-trip");
        }

        [Fact]
        public async Task An_unload_leaves_curated_a_food_the_catalog_admission_rule_admits_by_itself()
        {
            using var db = NewContext();
            var pasta = new IngredientEntity
            {
                Name = "Pasta with tomato sauce, no meat, canned", FdcId = "170397", FdcDataType = "sr_legacy_food",
                CurationStatusId = (long)CurationStatusEnum.PendingCuration,
                IngredientNutrients = new List<IngredientNutrientEntity> { new() { NutrientId = 1, Amount = 1 } },
            };
            var lemon = new IngredientEntity
            {
                Name = "Lemon peel, raw", FdcId = "167753", FdcDataType = "sr_legacy_food",
                CurationStatusId = (long)CurationStatusEnum.PendingCuration,
                IngredientNutrients = new List<IngredientNutrientEntity> { new() { NutrientId = 1, Amount = 1 } },
            };
            db.Ingredients.AddRange(pasta, lemon);
            await db.SaveChangesAsync();
            (await Wordings(db).LoadAsync(new[] { Row("canned spaghetti", pasta), Row("lemon rind", lemon) }, Batch, apply: true)).Promoted.Should().Be(2);
            db.ChangeTracker.Clear();
            var admitted = await db.Ingredients.SingleAsync(i => i.Id == pasta.Id);
            admitted.EnergyCheck = Nom.Data.Nutrition.EnergyCheckOutcome.General;
            await db.SaveChangesAsync();

            var unloaded = await Wordings(db).UnloadAsync(Batch, apply: true);

            unloaded.Demoted.Should().Be(1, "only the food the admission rule never judged goes back to awaiting curation");
            db.ChangeTracker.Clear();
            (await db.Ingredients.SingleAsync(i => i.Id == pasta.Id)).CurationStatusId.Should().Be((long)CurationStatusEnum.Curated,
                "the admission rule admits it by itself, so an unload of the batch that curated it first leaves it curated (#279327 on staging)");
            (await db.Ingredients.SingleAsync(i => i.Id == lemon.Id)).CurationStatusId.Should().Be((long)CurationStatusEnum.PendingCuration);
        }
    }
}

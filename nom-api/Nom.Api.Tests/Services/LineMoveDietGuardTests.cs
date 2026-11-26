using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
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
    /// Staging checkpoint 5, Q21: every path that moves a recipe line to another food passes the re-check's diet guard. The
    /// w01 unload moved 25 "Franco-American Spaghetti" lines off USDA canned pasta with tomato sauce onto an uncurated food of
    /// their name, and Nightshade Sensitivity stopped flagging them. Each path below is driven with a move that would lower a
    /// recipe's flag: the line stays, the move is counted as held, and the dry run counts what the apply does.
    /// </summary>
    public class LineMoveDietGuardTests
    {
        private const string Batch = "w01";
        private const string Franco = "1 can Franco-American Spaghetti";

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

        private static IngredientEntity Curated(string name, string? fdcId = null, string? description = null)
        {
            var food = new IngredientEntity
            {
                Name = name,
                FdcId = fdcId,
                CurationStatusId = (long)CurationStatusEnum.Curated,
                IngredientNutrients = new List<IngredientNutrientEntity> { new() { NutrientId = 1, Amount = 1 } },
            };
            if (description != null) food.Aliases.Add(new IngredientAliasEntity { AliasName = description, SourceContext = IngredientLinkService.UsdaDescriptionContext });
            return food;
        }

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

        private static VerifiedWordingRow Row(string wording, IngredientEntity food) =>
            new() { Row = wording, Wording = wording, Verdict = IngredientWordingVerdict.Food, FoodId = food.Id, FoodName = food.Name };

        private static async Task<RecipeEntity> ImportAsync(ApplicationDbContext db, string name, string line)
        {
            var currentUser = new Mock<ICurrentUserService>();
            currentUser.SetupGet(u => u.PersonId).Returns(7);
            var vetting = new Mock<IRecipeVettingService>();
            vetting.Setup(v => v.VetAsync(It.IsAny<ScraperRecipe>())).ReturnsAsync(new List<string>());
            var settings = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { [RecipeScrapingService.ResolveOnImportSetting] = "true" }).Build();
            var importer = new RecipeScrapingService(db, currentUser.Object, new Mock<IRecipeScraperClient>().Object, new Mock<IScrapingSourceService>().Object,
                vetting.Object, NullLogger<RecipeScrapingService>.Instance, settings);
            var recipeName = $"Imported {Guid.NewGuid():N}";
            var result = await importer.ImportStagedAsync(new StagedImportRequestModel
            {
                Recipes = { new ScraperRecipe { Name = recipeName, Ingredients = { new ScraperIngredient { Name = name, RawLine = line } } } },
            });
            result.Imported.Should().Be(1);
            db.ChangeTracker.Clear();
            return await db.Recipes.SingleAsync(r => r.Name == recipeName);
        }

        private static async Task<long> FoodOfAsync(ApplicationDbContext db, long recipeId)
        {
            db.ChangeTracker.Clear();
            return await db.RecipeIngredients.AsNoTracking().Where(ri => ri.RecipeId == recipeId).Select(ri => ri.IngredientId).SingleAsync();
        }

        private static async Task<string> LinesAsync(ApplicationDbContext db)
        {
            db.ChangeTracker.Clear();
            return string.Join("\n", await db.RecipeIngredients.IgnoreQueryFilters().AsNoTracking()
                .OrderBy(ri => ri.RecipeId).ThenBy(ri => ri.SortOrder)
                .Select(ri => $"{ri.RecipeId}:{ri.SortOrder}:{ri.IngredientId}:{ri.RawLine}:{ri.IsDeleted}")
                .ToListAsync());
        }

        private sealed record Spaghetti(IngredientEntity Sauce, IngredientEntity Franco, RecipeEntity Supper);

        /// <summary>The Q21 shape: an uncurated "franco-american spaghetti" whose line a w01 row moved onto canned pasta with tomato sauce.</summary>
        private static async Task<Spaghetti> LoadedAsync(ApplicationDbContext db)
        {
            var sauce = Curated("Pasta with tomato sauce, no meat, canned");
            var franco = Uncurated("franco-american spaghetti");
            db.Ingredients.AddRange(sauce, franco);
            await db.SaveChangesAsync();
            var supper = Scraped("Supper", (franco, Franco));
            db.Recipes.Add(supper);
            await db.SaveChangesAsync();
            (await Wordings(db).LoadAsync(new[] { Row("franco-american spaghetti", sauce) }, Batch, apply: true)).Rejected.Should().Be(0);
            (await Relink(db).LinkVerifiedWordingsAsync(apply: true, batch: Batch)).Moved.Should().Be(1, "onto the tomato food a move raises the flag, so the guard lets it through");
            (await FoodOfAsync(db, supper.Id)).Should().Be(sauce.Id);
            return new Spaghetti(sauce, franco, supper);
        }

        [Fact]
        public async Task The_guard_holds_a_move_off_a_food_that_names_tomato_and_lets_through_one_whose_line_or_usda_target_still_names_it()
        {
            using var db = NewContext();
            var sauce = Curated("Pasta with tomato sauce, no meat, canned");
            var franco = Uncurated("franco-american spaghetti");
            var usda = Curated("Spaghetti, canned", "279327", "Pasta with tomato sauce, no meat, canned");
            db.Ingredients.AddRange(sauce, franco, usda);
            await db.SaveChangesAsync();
            var guard = await LineMoveDietGuard.LoadAsync(db);

            (await guard.LoweredAsync(sauce.Id, franco.Id, Franco)).Should().Contain("Nightshade Sensitivity");
            (await guard.LoweredAsync(sauce.Id, franco.Id, "1 can Franco-American spaghetti in tomato sauce")).Should().BeEmpty("the line's own text still names the tomato");
            (await guard.LoweredAsync(franco.Id, sauce.Id, Franco)).Should().BeEmpty("a move onto the tomato food raises the flag");
            (await guard.LoweredAsync(sauce.Id, usda.Id, Franco)).Should().BeEmpty("a USDA food with its description is its own statement of what it is");
            (await LineMoveDietGuard.Decided.LoweredAsync(sauce.Id, franco.Id, Franco)).Should().BeEmpty();
            guard.Held.Should().Be(0, "asking counts nothing");
        }

        [Fact]
        public async Task An_unload_with_lines_leaves_a_line_whose_move_back_would_lower_its_tomato_flag_so_unload_and_reload_leave_the_lines_as_they_were()
        {
            using var db = NewContext();
            var k = await LoadedAsync(db);
            var before = await LinesAsync(db);

            var dry = await Wordings(db).UnloadAsync(Batch, apply: false, withLines: true);
            var unloaded = await Wordings(db).UnloadAsync(Batch, apply: true, withLines: true);

            dry.Lines!.HeldByDiet.Should().Be(1, "the dry run counts what the apply holds");
            unloaded.Lines!.HeldByDiet.Should().Be(1);
            unloaded.Lines.Undone!.Restored.Should().Be(0);
            (await FoodOfAsync(db, k.Supper.Id)).Should().Be(k.Sauce.Id, "staging checkpoint 5, Q21: back on the uncurated food Nightshade Sensitivity no longer flagged the recipe");

            await Wordings(db).LoadAsync(new[] { Row("franco-american spaghetti", k.Sauce) }, Batch, apply: true);
            await Relink(db).LinkVerifiedWordingsAsync(apply: true, batch: Batch);
            (await LinesAsync(db)).Should().Be(before, "the round trip leaves every line where it was");
        }

        [Fact]
        public async Task An_undo_puts_back_a_move_that_raised_flags_only_by_the_operators_explicit_choice_listed_by_its_dry_run_and_journaled()
        {
            using var db = NewContext();
            var since = DateTime.UtcNow.AddSeconds(-1);
            var k = await LoadedAsync(db);

            var held = await Relink(db).UndoAsync(since, apply: false);
            (held.HeldByDiet, held.RestoredLowering, held.RestoreLowered).Should().Be((1, 0, false), "by default the way back that lowers the tomato flag is held");
            var dry = await Relink(db).UndoAsync(since, apply: false, restoreLowered: true);
            (dry.HeldByDiet, dry.RestoredLowering, dry.RestoreLowered).Should().Be((0, 1, true));
            dry.LoweringMoves.Should().ContainSingle(m => m.RecipeId == k.Supper.Id && m.FromId == k.Sauce.Id && m.ToId == k.Franco.Id && m.Types.Contains("Nightshade Sensitivity"),
                "the dry run lists every move the operator's choice would let lower a flag");
            (await FoodOfAsync(db, k.Supper.Id)).Should().Be(k.Sauce.Id, "a dry run writes nothing");

            var applied = await Relink(db).UndoAsync(since, apply: true, restoreLowered: true);

            (applied.Restored, applied.RestoredLowering, applied.HeldByDiet).Should().Be((1, 1, 0));
            (await FoodOfAsync(db, k.Supper.Id)).Should().Be(k.Franco.Id, "staging checkpoint 7, S9: the undo restores the exact journaled earlier state when the operator chooses it");
            var journal = await db.AuditLogEntries.AsNoTracking().SingleAsync(a => a.ChangeType == LineRelinkService.LoweredByUndoChangeType);
            (journal.EntityType, journal.EntityId, journal.OldValue).Should().Be((LineRelinkService.LoweredByUndoEntityType, k.Supper.Id, LineRelinkService.Place(k.Sauce.Id, 0)));
            journal.NewValue.Should().Contain("Nightshade Sensitivity");
        }

        [Fact]
        public async Task An_undo_that_chose_to_restore_journals_no_lowering_for_a_move_the_writer_could_not_make()
        {
            using var db = NewContext();
            var since = DateTime.UtcNow.AddSeconds(-1);
            var k = await LoadedAsync(db);
            db.Ingredients.Add(Uncurated("franco-american spaghetti"));
            await db.SaveChangesAsync();

            var applied = await Relink(db).UndoAsync(since, apply: true, restoreLowered: true);

            (await FoodOfAsync(db, k.Supper.Id)).Should().Be(k.Sauce.Id, "the retired food cannot come back under a name another food took, so the line has nowhere to go");
            (applied.Restored, applied.RestoredLowering).Should().Be((0, 0), "a move the writer did not make lowered nothing (review ck16-r3)");
            applied.LoweringMoves.Should().BeEmpty();
            (await db.AuditLogEntries.AsNoTracking().AnyAsync(a => a.ChangeType == LineRelinkService.LoweredByUndoChangeType)).Should().BeFalse();
        }

        private sealed class StopOnLine : SaveChangesInterceptor
        {
            public long? Recipe { get; set; }

            public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
            {
                if (Recipe is { } id && eventData.Context!.ChangeTracker.Entries<RecipeIngredientEntity>().Any(e => e.State == EntityState.Added && e.Entity.RecipeId == id))
                    throw new InvalidOperationException("the process stops");
                return base.SavingChangesAsync(eventData, result, cancellationToken);
            }
        }

        [Fact]
        public async Task An_undo_that_restores_lowering_moves_journals_each_with_its_move_so_a_stop_part_way_leaves_none_unjournaled()
        {
            var stop = new StopOnLine();
            using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .AddInterceptors(stop)
                .Options);
            var since = DateTime.UtcNow.AddSeconds(-1);
            var sauce = Curated("Pasta with tomato sauce, no meat, canned");
            var franco = Uncurated("franco-american spaghetti");
            db.Ingredients.AddRange(sauce, franco);
            await db.SaveChangesAsync();
            db.Recipes.AddRange(Enumerable.Range(0, 510).Select(i => Scraped("Supper " + i, (franco, Franco))));
            await db.SaveChangesAsync();
            await Wordings(db).LoadAsync(new[] { Row("franco-american spaghetti", sauce) }, Batch, apply: true);
            (await Relink(db).LinkVerifiedWordingsAsync(apply: true, batch: Batch)).Moved.Should().Be(510);
            stop.Recipe = await db.AuditLogEntries.AsNoTracking()
                .Where(a => a.EntityType == LineRelinkService.RelinkEntityType && a.ChangeType == LineRelinkService.RelinkChangeType)
                .OrderBy(a => a.Id).Select(a => a.EntityId).FirstAsync();
            db.ChangeTracker.Clear();

            await FluentActions.Awaiting(() => Relink(db).UndoAsync(since, apply: true, restoreLowered: true)).Should().ThrowAsync<InvalidOperationException>();

            stop.Recipe = null;
            db.ChangeTracker.Clear();
            var back = await db.RecipeIngredients.AsNoTracking().CountAsync(ri => ri.IngredientId == franco.Id);
            back.Should().BeGreaterThan(500, "the first chunk and part of the second were put back before the process stopped");
            (await db.AuditLogEntries.AsNoTracking().CountAsync(a => a.ChangeType == LineRelinkService.LoweredByUndoChangeType)).Should().Be(back,
                "every line put back with a lowered flag is journaled with its move, never after the whole sweep (review ck16-r2)");
        }

        [Fact]
        public async Task A_take_back_of_a_plain_drink_lowers_no_severe_type_its_old_food_flags_by_a_statement()
        {
            using var db = NewContext();
            var blend = new IngredientEntity { Name = "Evening Blend", CurationStatusId = (long)CurationStatusEnum.Curated, FdcCategoryId = 22 };
            db.Ingredients.Add(blend);
            await db.SaveChangesAsync();
            await DefaultRestrictionCriteria.EnsureAsync(db);
            await new PreparedContentsService(db).RecordAsync(new[] { new ContentsStatement(blend.Id, PreparedContentsService.FdcSource("100001"), "Irish whiskey, milk, sugar") }, "fdc-contents-test", apply: true);
            (await db.Ingredients.SingleAsync(i => i.Id == blend.Id)).FdcCategoryId = 14;
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            var guard = await LineMoveDietGuard.LoadAsync(db);

            (await guard.SevereLoweredToNameAsync(blend.Id, "the blend", "1 cup of the blend")).Should().Contain("Milk Allergy", "the statement's milk is read from the food's data, not its name");
            (await guard.SevereLoweredToNameAsync(blend.Id, "brandy", "1 cup brandy")).Should().BeEmpty("a plain drink's own name accounts for every type (review ck16-r4)");
        }

        [Fact]
        public async Task An_unloads_dry_run_counts_what_its_apply_does_with_a_food_it_brings_back_and_a_landing_it_moves_back_onto_that_food()
        {
            using var db = NewContext();
            var sauce = Curated("Tomato Sauce");
            var mix = Uncurated("tomato soup mix");
            db.Ingredients.AddRange(sauce, mix);
            await db.SaveChangesAsync();
            var soup = Scraped("Soup", (mix, "1 packet tomato soup mix"));
            db.Recipes.Add(soup);
            await db.SaveChangesAsync();
            await Wordings(db).LoadAsync(new[] { Row("tomato soup mix", sauce) }, Batch, apply: true);
            (await Relink(db).LinkVerifiedWordingsAsync(apply: true, batch: Batch)).Moved.Should().Be(1);
            db.ChangeTracker.Clear();
            (await db.Ingredients.IgnoreQueryFilters().AsNoTracking().SingleAsync(i => i.Id == mix.Id)).IsDeleted.Should().BeTrue("the relink retired the uncurated food of the wording");
            var landed = await ImportAsync(db, "tomato soup mix", "1 packet tomato soup mix");
            (await FoodOfAsync(db, landed.Id)).Should().Be(sauce.Id);

            var dry = await Wordings(db).UnloadAsync(Batch, apply: false, withLines: true);
            var applied = await Wordings(db).UnloadAsync(Batch, apply: true, withLines: true);

            (applied.Lines!.Undone!.Restored, applied.Lines.HeldByDiet, applied.Lines.LandingsMovedBack).Should().Be((1, 0, 1),
                "brought back, the uncurated food still names the tomato, so neither the move nor the landing lowers a flag");
            (dry.Lines!.HeldByDiet, dry.Lines.LandingsMovedBack, dry.Lines.LandingsKept, dry.Lines.LandingsSkipped)
                .Should().Be((applied.Lines.HeldByDiet, applied.Lines.LandingsMovedBack, applied.Lines.LandingsKept, applied.Lines.LandingsSkipped),
                    "staging checkpoint 7, S10: the dry run read the retired food as dead and counted no landing moved back, so it differed from its apply");
            (await FoodOfAsync(db, soup.Id)).Should().Be(mix.Id);
            (await FoodOfAsync(db, landed.Id)).Should().Be(mix.Id);
        }

        [Fact]
        public async Task A_retract_holds_the_line_move_and_the_import_landing_whose_move_back_would_lower_the_flag()
        {
            using var db = NewContext();
            var k = await LoadedAsync(db);
            var landed = await ImportAsync(db, "franco-american spaghetti", Franco);
            (await FoodOfAsync(db, landed.Id)).Should().Be(k.Sauce.Id, "the import lands through the wording");
            var id = await db.IngredientWordings.Select(w => w.Id).SingleAsync();

            var dry = await Wordings(db).RetractAsync(id, "test", apply: false);
            var retracted = await Wordings(db).RetractAsync(id, "test", apply: true);

            dry!.Lines.HeldByDiet.Should().Be(2);
            retracted!.Lines.HeldByDiet.Should().Be(2);
            retracted.Lines.LandingsMovedBack.Should().Be(0);
            (await FoodOfAsync(db, k.Supper.Id)).Should().Be(k.Sauce.Id);
            (await FoodOfAsync(db, landed.Id)).Should().Be(k.Sauce.Id);
        }

        [Fact]
        public async Task A_reload_holds_the_return_of_an_import_landing_that_would_lower_its_flags()
        {
            using var db = NewContext();
            var strands = Curated("Konjac Strands");
            db.Ingredients.Add(strands);
            await db.SaveChangesAsync();
            await Wordings(db).LoadAsync(new[] { Row("spaghetti", strands) }, Batch, apply: true);
            var landed = await ImportAsync(db, "spaghetti", "a handful of the good stuff");
            (await FoodOfAsync(db, landed.Id)).Should().Be(strands.Id);
            var id = await db.IngredientWordings.Select(w => w.Id).SingleAsync();
            (await Wordings(db).RetractAsync(id, "test", apply: true))!.Lines.LandingsMovedBack.Should().Be(1, "onto a food named spaghetti a move raises the wheat flags");
            var home = await FoodOfAsync(db, landed.Id);
            home.Should().NotBe(strands.Id);
            await Wordings(db).LoadAsync(new[] { Row("spaghetti", strands) }, "w02", apply: true, overrideRejections: true);

            var dry = await Relink(db).LinkVerifiedWordingsAsync(apply: false, batch: "w02");
            var applied = await Relink(db).LinkVerifiedWordingsAsync(apply: true, batch: "w02");

            (dry.HeldByDiet, dry.LandingsReturned).Should().Be((1, 0));
            (applied.HeldByDiet, applied.LandingsReturned).Should().Be((1, 0));
            (await FoodOfAsync(db, landed.Id)).Should().Be(home, "on Konjac Strands Celiac Disease and Wheat Allergy would no longer read the line");
        }

        [Fact]
        public async Task A_relink_by_name_a_food_move_and_a_wording_move_each_hold_a_move_that_would_lower_a_flag()
        {
            using var db = NewContext();
            var tomato = Curated("Tomato Sauce");
            var marinara = Curated("Marinara");
            db.Ingredients.AddRange(tomato, marinara);
            await db.SaveChangesAsync();
            var pasta = Scraped("Pasta", (tomato, "1 cup marinara"));
            db.Recipes.Add(pasta);
            await db.SaveChangesAsync();

            var preview = await Relink(db).PreviewAsync();
            var relinked = await Relink(db).ApplyAsync();
            var moved = await Relink(db).MoveFoodAsync(tomato.Id, marinara.Id);
            var wordingDry = await Relink(db).MoveWordingsAsync(new[] { new LineRelinkWordingMove { FromIngredientId = tomato.Id, Wording = "marinara", ToIngredientId = marinara.Id } });
            var wording = await Relink(db).MoveWordingsAsync(new[] { new LineRelinkWordingMove { FromIngredientId = tomato.Id, Wording = "marinara", ToIngredientId = marinara.Id } }, apply: true);

            (preview.MoveCount, preview.HeldByDiet).Should().Be((0, 1));
            (relinked.Moved, relinked.HeldByDiet).Should().Be((0, 1));
            relinked.DietHolds.Single().Types.Should().Contain("Nightshade Sensitivity");
            (moved!.Moved, moved.HeldByDiet).Should().Be((0, 1));
            (wordingDry.Remaining, wordingDry.HeldByDiet).Should().Be((0, 1));
            (wording.Moved, wording.HeldByDiet).Should().Be((0, 1));
            (await FoodOfAsync(db, pasta.Id)).Should().Be(tomato.Id);
        }

        [Fact]
        public async Task The_relink_undo_sweep_holds_a_line_move_and_a_picked_alternative_whose_way_back_would_lower_a_flag_in_its_dry_run_and_its_apply()
        {
            using var db = NewContext();
            var sauce = Curated("Pasta with tomato sauce, no meat, canned");
            var franco = Uncurated("franco-american spaghetti");
            var plain = Curated("Spaghetti sauce, plain");
            db.Ingredients.AddRange(sauce, franco, plain);
            await db.SaveChangesAsync();
            var supper = Scraped("Supper", (sauce, Franco));
            var lunch = Scraped("Lunch", (sauce, "1 can Franco-American or plain spaghetti sauce"));
            db.Recipes.AddRange(supper, lunch);
            await db.SaveChangesAsync();
            var since = DateTime.UtcNow.AddMinutes(-1);
            db.AuditLogEntries.AddRange(
                new AuditLogEntryEntity
                {
                    EntityType = LineRelinkService.RelinkEntityType, EntityId = supper.Id, ChangeType = LineRelinkService.RelinkChangeType,
                    PropertyName = nameof(RecipeIngredientEntity.IngredientId), OldValue = LineRelinkService.Place(franco.Id, 0), NewValue = LineRelinkService.Place(sauce.Id, 0),
                    Timestamp = DateTime.UtcNow,
                },
                new AuditLogEntryEntity
                {
                    EntityType = LineRelinkService.RelinkEntityType, EntityId = lunch.Id, ChangeType = CompoundLineService.AlternativeChangeType,
                    PropertyName = nameof(RecipeIngredientEntity.IngredientId), OldValue = LineRelinkService.Place(plain.Id, 0), NewValue = LineRelinkService.Place(sauce.Id, 0),
                    Timestamp = DateTime.UtcNow,
                });
            await db.SaveChangesAsync();
            var before = await LinesAsync(db);

            var dry = await Relink(db).UndoAsync(since);
            var undone = await Relink(db).UndoAsync(since, apply: true);

            (dry.Restored, dry.AlternativesPutBack, dry.HeldByDiet, dry.Skipped).Should().Be((0, 0, 2, 0));
            (undone.Restored, undone.AlternativesPutBack, undone.HeldByDiet, undone.Skipped).Should().Be((0, 0, 2, 0));
            (await LinesAsync(db)).Should().Be(before);
            (await db.AuditLogEntries.CountAsync(a => a.EntityType == LineRelinkService.RelinkEntityType)).Should().Be(2, "a held entry stays, so a later undo can still take it back");
        }

        [Fact]
        public async Task A_link_recheck_undo_holds_a_line_whose_way_back_would_lower_a_flag_in_its_dry_run_and_its_apply()
        {
            using var db = NewContext();
            var sauce = Curated("Pasta with tomato sauce, no meat, canned");
            var franco = Uncurated("franco-american spaghetti");
            db.Ingredients.AddRange(sauce, franco);
            await db.SaveChangesAsync();
            var supper = Scraped("Supper", (sauce, Franco));
            db.Recipes.Add(supper);
            await db.SaveChangesAsync();
            var stamp = new DateTime(2026, 11, 23, 21, 52, 3, DateTimeKind.Utc);
            db.AuditLogEntries.AddRange(
                new AuditLogEntryEntity { EntityType = LinkRecheckService.EntityType, EntityId = 0, ChangeType = LinkRecheckService.RunChangeType, PropertyName = LinkRecheckRules.Prepared, Timestamp = stamp },
                new AuditLogEntryEntity
                {
                    EntityType = LineRelinkService.RelinkEntityType, EntityId = supper.Id, ChangeType = LinkRecheckService.LineChangeType,
                    PropertyName = nameof(RecipeIngredientEntity.IngredientId), OldValue = LineRelinkService.Place(franco.Id, 0), NewValue = LineRelinkService.Place(sauce.Id, 0),
                    Timestamp = stamp,
                });
            await db.SaveChangesAsync();
            var recheck = new LinkRecheckService(db, new NoUser(), new LinkAutoApplyService(db, new MemoryCache(new MemoryCacheOptions())), Relink(db),
                new LaneFrameService(db, Array.Empty<ILaneUndo>(), NullLogger<LaneFrameService>.Instance), NullLogger<LinkRecheckService>.Instance);

            var dry = await recheck.UndoAsync(stamp);
            var undone = await recheck.UndoAsync(stamp, apply: true);

            (dry.LinesRestored, dry.HeldByDiet).Should().Be((0, 1));
            (undone.LinesRestored, undone.HeldByDiet).Should().Be((0, 1));
            (await FoodOfAsync(db, supper.Id)).Should().Be(sauce.Id);
        }

        [Fact]
        public async Task A_food_merge_is_refused_when_moving_its_lines_would_lower_a_flag()
        {
            using var db = NewContext();
            var tomato = Curated("Tomato Sauce");
            var marinara = Curated("Marinara");
            db.Ingredients.AddRange(tomato, marinara);
            await db.SaveChangesAsync();
            db.Recipes.Add(Scraped("Pasta", (tomato, "1 cup marinara")));
            await db.SaveChangesAsync();

            (await new CatalogCleanupService(db, new NoUser()).MergeIntoAsync(tomato.Id, marinara.Id, null)).Should().BeFalse(
                "the link apply then keeps the line on its own food and copies the nutrients instead");
        }
    

        [Fact]
        public async Task A_link_whose_merge_the_guard_holds_is_left_pending_and_copies_no_nutrients_onto_the_food()
        {
            using var db = NewContext();
            var tomato = Uncurated("tomato pasta");
            var usda = Curated("Pasta, cooked, enriched", "168927");
            db.Ingredients.AddRange(tomato, usda);
            await db.SaveChangesAsync();
            var supper = Scraped("Supper", (tomato, "8 oz pasta"));
            db.Recipes.Add(supper);
            var proposal = new FoodCatalogProposalEntity
            {
                Action = FoodProposalAction.Update, IngredientId = tomato.Id, Field = FoodCatalogReviewService.FdcLinkField, CurrentValue = "tomato pasta",
                FdcId = usda.FdcId, ProposedValue = usda.Id.ToString(CultureInfo.InvariantCulture), Source = "ai:fake", Batch = IngredientLinkService.Batch,
                Status = FoodProposalStatus.Pending,
            };
            db.FoodCatalogProposals.Add(proposal);
            await db.SaveChangesAsync();
            var cleanup = new CatalogCleanupService(db, new NoUser());
            (await cleanup.LowersDietAsync(tomato.Id, usda.Id)).Should().BeTrue("the USDA pasta names no tomato, nor does the line");

            var applied = await new FoodCatalogReviewService(db, new Mock<IFoodCatalogAuditService>().Object, cleanup).ApplyProposalAsync(proposal.Id, reviewerPersonId: 2);

            applied.Should().BeFalse("review CK10S-2: a refused merge fell through to copying the plain pasta's nutrients onto the tomato pasta");
            db.ChangeTracker.Clear();
            (await db.FoodCatalogProposals.SingleAsync()).Status.Should().Be(FoodProposalStatus.Pending);
            (await db.IngredientNutrients.CountAsync(n => n.IngredientId == tomato.Id)).Should().Be(0);
            (await db.Ingredients.SingleAsync(i => i.Id == tomato.Id)).IsDeleted.Should().BeFalse();
            (await FoodOfAsync(db, supper.Id)).Should().Be(tomato.Id);
        }

        [Fact]
        public async Task A_reload_counts_a_held_return_once_when_the_line_names_its_wording()
        {
            using var db = NewContext();
            var home = Uncurated("pasta sauce");
            home.Aliases.Add(new IngredientAliasEntity { AliasName = "Sauce, pasta, spaghetti/marinara, tomato", SourceContext = IngredientLinkService.UsdaDescriptionContext });
            var alfredo = Curated("Alfredo Base");
            db.Ingredients.AddRange(home, alfredo);
            await db.SaveChangesAsync();
            var supper = Scraped("Supper", (home, "1 cup pasta sauce"));
            db.Recipes.Add(supper);
            await db.SaveChangesAsync();
            db.AuditLogEntries.Add(new AuditLogEntryEntity
            {
                EntityType = LineRelinkService.RelinkEntityType, EntityId = supper.Id, ChangeType = LineRelinkService.WordingRetractChangeType,
                PropertyName = nameof(RecipeIngredientEntity.IngredientId), OldValue = LineRelinkService.Place(alfredo.Id, 0), NewValue = LineRelinkService.Place(home.Id, 0),
                Timestamp = DateTime.UtcNow.AddMinutes(-5),
            });
            await db.SaveChangesAsync();
            await Wordings(db).LoadAsync(new[] { Row("pasta sauce", alfredo) }, Batch, apply: true);

            var dry = await Relink(db).LinkVerifiedWordingsAsync(apply: false, batch: Batch);
            var applied = await Relink(db).LinkVerifiedWordingsAsync(apply: true, batch: Batch);

            (dry.HeldByDiet, dry.LandingsReturned, dry.Remaining).Should().Be((1, 0, 0), "review CK10S-5: the held return was held and counted again as a source line");
            (applied.HeldByDiet, applied.LandingsReturned, applied.Moved).Should().Be((1, 0, 0));
            (await FoodOfAsync(db, supper.Id)).Should().Be(home.Id);
        }

        [Fact]
        public async Task The_relink_undo_holds_an_unsplit_whose_kept_row_would_go_back_to_a_food_that_lowers_a_flag()
        {
            using var db = NewContext();
            var sauce = Curated("Pasta with tomato sauce, no meat, canned");
            var franco = Uncurated("franco-american spaghetti");
            var salt = Curated("Salt");
            db.Ingredients.AddRange(sauce, franco, salt);
            await db.SaveChangesAsync();
            var supper = new RecipeEntity
            {
                Name = "Supper",
                ScrapedAtUtc = DateTime.UtcNow,
                RecipeIngredients = new List<RecipeIngredientEntity>
                {
                    new() { IngredientId = sauce.Id, SortOrder = 0, Quantity = 1, MeasurementId = 1, RawLine = Franco },
                    new() { IngredientId = salt.Id, SortOrder = 1, Quantity = 0, MeasurementId = 1, RawLine = "salt" },
                },
            };
            db.Recipes.Add(supper);
            await db.SaveChangesAsync();
            var since = DateTime.UtcNow.AddMinutes(-1);
            string Journal(long food, int sortOrder, decimal quantity, string raw) =>
                string.Create(CultureInfo.InvariantCulture, $$"""{"i":{{food}},"s":{{sortOrder}},"q":{{quantity}},"m":1,"r":"{{raw}}"}""");
            db.AuditLogEntries.Add(new AuditLogEntryEntity
            {
                EntityType = LineRelinkService.RelinkEntityType, EntityId = supper.Id, ChangeType = CompoundLineService.SplitChangeType,
                PropertyName = nameof(RecipeIngredientEntity.RawLine), OldValue = Journal(franco.Id, 0, 1, Franco + " and salt"),
                NewValue = "[" + Journal(sauce.Id, 0, 1, Franco) + "," + Journal(salt.Id, 1, 0, "salt") + "]",
                Timestamp = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
            var before = await LinesAsync(db);

            var dry = await Relink(db).UndoAsync(since);
            var undone = await Relink(db).UndoAsync(since, apply: true);

            (dry.Unsplit, dry.HeldByDiet, dry.Skipped).Should().Be((0, 1, 0));
            (undone.Unsplit, undone.HeldByDiet, undone.Skipped).Should().Be((0, 1, 0), "review CK10S-6: the unsplit passes the guard with the joined line it puts back");
            (await LinesAsync(db)).Should().Be(before);
        }

        [Fact]
        public async Task The_guard_holds_a_move_off_a_made_product_whose_brands_are_split_as_the_store_holds_its_recipe_unknown()
        {
            using var db = NewContext();
            var cubes = Curated("Chicken Bouillon Cubes", "171563");
            var salt = Curated("Salt");
            db.Ingredients.AddRange(cubes, salt);
            await db.SaveChangesAsync();
            BrandedConsensus.ForFood(cubes.Name, cubes.FdcId).Should().Contain(r => r.Class == "wheat" && r.Verdict == BrandedConsensus.Split);
            var guard = await LineMoveDietGuard.LoadAsync(db);

            (await guard.LoweredAsync(cubes.Id, salt.Id, "2 cubes")).Should().Contain("Wheat Allergy", "review ck18-guard-ignores-split: the store holds a recipe on a split food as unknown, so a move to a clear food lowers it");
            (await guard.LoweredAsync(salt.Id, cubes.Id, "2 cubes")).Should().BeEmpty("a move onto the split food raises the hold");
        }
    }
}

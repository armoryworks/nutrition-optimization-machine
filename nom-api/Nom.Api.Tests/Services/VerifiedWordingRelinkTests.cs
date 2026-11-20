using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Nom.Data;
using Nom.Data.Audit;
using Nom.Data.Curation;
using Nom.Data.Nutrient;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Curation;
using Nom.Orch.Services;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// Settling the lines of an uncurated ingredient through the verified wording table, and the
    /// journaled rename that names a food before wordings point at it. Wordings are staging's.
    /// </summary>
    public class VerifiedWordingRelinkTests
    {
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

        private static IngredientEntity CuratedFood(string name, bool nutrition = true) => new()
        {
            Name = name,
            CurationStatusId = (long)CurationStatusEnum.Curated,
            IngredientNutrients = nutrition ? new List<IngredientNutrientEntity> { new() { NutrientId = 1, Amount = 1 } } : new List<IngredientNutrientEntity>(),
        };

        private static IngredientEntity Uncurated(string name) => new() { Name = name, CurationStatusId = (long)CurationStatusEnum.NonCurated };

        private static RecipeEntity Recipe(string name, DateTime? scraped, params (IngredientEntity Food, string Line, string? Kind)[] lines) => new()
        {
            Name = name,
            ScrapedAtUtc = scraped,
            RecipeIngredients = lines.Select((l, i) => new RecipeIngredientEntity
            {
                IngredientId = l.Food.Id, SortOrder = i, Quantity = 1, MeasurementId = 1, RawLine = l.Line, LineKind = l.Kind,
            }).ToList(),
        };

        private static IngredientWordingEntity Wording(string wording, string verdict, IngredientEntity? food = null) =>
            new() { Wording = wording, Verdict = verdict, IngredientId = food?.Id, Batch = "w01", Source = "verified:w01" };

        private static FoodCatalogProposalEntity OpenLink(IngredientEntity ingredient) => new()
        {
            IngredientId = ingredient.Id, Field = FoodCatalogReviewService.FdcLinkField, Status = FoodProposalStatus.Pending,
            Batch = IngredientLinkService.Batch, Source = "ai:fake:1b",
        };

        [Fact]
        public async Task A_verified_wording_plans_only_the_scraped_ingredient_lines_of_its_uncurated_ingredient()
        {
            using var db = NewContext();
            var extract = CuratedFood("Vanilla Extract");
            var thyme = CuratedFood("Fresh Thyme", nutrition: false);
            var peel = CuratedFood("Lemon Peel");
            var vanilla = Uncurated("vanilla");
            var rind = Uncurated("lemon rind");
            var bareThyme = Uncurated("thyme");
            var wine = Uncurated("white wine");
            db.Ingredients.AddRange(extract, thyme, peel, vanilla, rind, bareThyme, wine);
            await db.SaveChangesAsync();
            db.IngredientWordings.AddRange(Wording("vanilla", "food", extract), Wording("lemon rind", "food", peel),
                Wording("thyme", "food", thyme), Wording("white wine", "none"));
            db.Recipes.AddRange(
                Recipe("Scraped", DateTime.UtcNow, (vanilla, "1 tsp. vanilla", null), (vanilla, "Vanilla", RecipeIngredientLineKind.Equipment),
                    (rind, "grated rind of 1 lemon", RecipeIngredientLineKind.Ingredient), (bareThyme, "a sprig of thyme", null), (wine, "1 cup white wine", null)),
                Recipe("Written by a member", null, (vanilla, "1 tsp vanilla", null)));
            db.FoodCatalogProposals.Add(OpenLink(rind));
            await db.SaveChangesAsync();

            var plan = await new LineRelinkService(db, new NoUser()).LinkVerifiedWordingsAsync();

            plan.Should().Match<VerifiedWordingLinkResult>(r => !r.Applied && r.Wordings == 2 && r.TargetsUnusable == 1
                && r.Sources == 2 && r.Remaining == 2 && r.Moved == 0 && r.LeftBehind == 0 && r.SourcesRetired == 1 && r.SourcesKept == 1 && r.ProposalsWithdrawn == 1);
            plan.Samples.Select(m => (m.FromName, m.Wording, m.ToName, m.Lines)).Should().BeEquivalentTo(new[]
            {
                ("vanilla", "vanilla", "Vanilla Extract", 1),
                ("lemon rind", "lemon rind", "Lemon Peel", 1),
            }, "\"grated rind of 1 lemon\" normalises to the wording lemon rind");
            (await db.Ingredients.CountAsync()).Should().Be(7);
            (await db.FoodCatalogProposals.CountAsync()).Should().Be(1);
            (await db.AuditLogEntries.AnyAsync()).Should().BeFalse();
        }

        [Fact]
        public async Task A_line_whose_own_text_does_not_name_the_wording_stays_on_the_uncurated_row()
        {
            using var db = NewContext();
            var gelatin = CuratedFood("Gelatin");
            var gelatine = Uncurated("gelatine");
            var boxed = Uncurated("box of gelatine");
            db.Ingredients.AddRange(gelatin, gelatine, boxed);
            await db.SaveChangesAsync();
            db.IngredientWordings.AddRange(Wording("gelatine", "food", gelatin), Wording("box of gelatine", "food", gelatin));
            db.Recipes.AddRange(
                Recipe("27601", DateTime.UtcNow, (gelatine, "one gallon of water", null), (gelatine, "one ounce of gelatine", null)),
                Recipe("56975", DateTime.UtcNow, (boxed, "one-half pint cold water", null), (boxed, "1 box of gelatine", null)));
            await db.SaveChangesAsync();
            var service = new LineRelinkService(db, new NoUser());

            var applied = await service.LinkVerifiedWordingsAsync(apply: true);

            applied.Should().Match<VerifiedWordingLinkResult>(r => r.Moved == 2 && r.LeftBehind == 2 && r.SourcesRetired == 0 && r.SourcesKept == 2);
            var lines = await db.RecipeIngredients.AsNoTracking().ToListAsync();
            lines.Where(l => l.IngredientId == gelatin.Id).Select(l => l.RawLine).Should().BeEquivalentTo("one ounce of gelatine", "1 box of gelatine");
            lines.Single(l => l.RawLine == "one gallon of water").IngredientId.Should().Be(gelatine.Id);
            lines.Single(l => l.RawLine == "one-half pint cold water").IngredientId.Should().Be(boxed.Id);
        }

        [Fact]
        public async Task A_wording_whose_food_is_no_longer_curated_plans_nothing()
        {
            using var db = NewContext();
            var extract = CuratedFood("Vanilla Extract");
            extract.CurationStatusId = (long)CurationStatusEnum.PendingCuration;
            var vanilla = Uncurated("vanilla");
            db.Ingredients.AddRange(extract, vanilla);
            await db.SaveChangesAsync();
            db.IngredientWordings.Add(Wording("vanilla", "food", extract));
            db.Recipes.Add(Recipe("Scraped", DateTime.UtcNow, (vanilla, "1 tsp. vanilla", null)));
            await db.SaveChangesAsync();

            var plan = await new LineRelinkService(db, new NoUser()).LinkVerifiedWordingsAsync();

            plan.Should().Match<VerifiedWordingLinkResult>(r => r.Wordings == 0 && r.TargetsUnusable == 1 && r.Sources == 0 && r.Remaining == 0);
        }

        [Fact]
        public async Task An_uncurated_ingredient_a_verified_wording_settles_is_retired_with_its_open_proposal_and_an_undo_brings_it_back()
        {
            using var db = NewContext();
            var extract = CuratedFood("Vanilla Extract");
            var vanilla = Uncurated("vanilla");
            db.Ingredients.AddRange(extract, vanilla);
            await db.SaveChangesAsync();
            db.IngredientWordings.Add(Wording("vanilla", "food", extract));
            db.FoodCatalogProposals.Add(OpenLink(vanilla));
            await db.SaveChangesAsync();
            var service = new LineRelinkService(db, new NoUser());
            var started = DateTime.UtcNow;

            var applied = await service.LinkVerifiedWordingsAsync(apply: true);

            applied.Should().Match<VerifiedWordingLinkResult>(r => r.Applied && r.SourcesRetired == 1 && r.ProposalsWithdrawn == 1);
            (await db.Ingredients.IgnoreQueryFilters().SingleAsync(i => i.Id == vanilla.Id)).Should().Match<IngredientEntity>(i =>
                i.IsDeleted && i.Name == $"vanilla (deleted #{vanilla.Id})");
            (await db.FoodCatalogProposals.AnyAsync()).Should().BeFalse();
            (await db.AuditLogEntries.SingleAsync(a => a.ChangeType == LineRelinkService.RetireChangeType)).Should().Match<AuditLogEntryEntity>(a =>
                a.EntityId == vanilla.Id && a.NewValue == $"{extract.Id}@0");

            var undone = await service.UndoAsync(started, apply: true);

            undone.FoodsRestored.Should().Be(1);
            undone.ProposalsRestored.Should().Be(1);
            (await db.Ingredients.SingleAsync(i => i.Id == vanilla.Id)).Name.Should().Be("vanilla");
        }

        [Fact]
        public async Task The_proposals_withdrawn_with_a_retired_ingredient_are_journaled_and_the_undo_brings_back_pending_and_rejected_alike()
        {
            using var db = NewContext();
            var extract = CuratedFood("Vanilla Extract");
            var vanilla = Uncurated("vanilla");
            db.Ingredients.AddRange(extract, vanilla);
            await db.SaveChangesAsync();
            db.IngredientWordings.Add(Wording("vanilla", "food", extract));
            var pending = OpenLink(vanilla);
            var rejected = OpenLink(vanilla);
            rejected.Status = FoodProposalStatus.Rejected;
            rejected.Reason = "No USDA match";
            db.FoodCatalogProposals.AddRange(pending, rejected);
            await db.SaveChangesAsync();
            var service = new LineRelinkService(db, new NoUser());
            var started = DateTime.UtcNow;

            (await service.LinkVerifiedWordingsAsync(apply: true)).ProposalsWithdrawn.Should().Be(2);

            (await db.AuditLogEntries.Where(a => a.ChangeType == LineRelinkService.ProposalWithdrawChangeType).Select(a => a.EntityId).ToListAsync())
                .Should().BeEquivalentTo(new[] { pending.Id, rejected.Id });
            (await db.FoodCatalogProposals.AnyAsync()).Should().BeFalse();

            var undone = await service.UndoAsync(started, apply: true);

            (undone.FoodsRestored, undone.ProposalsRestored, undone.Skipped).Should().Be((1, 2, 0));
            (await db.FoodCatalogProposals.OrderBy(p => p.Id).Select(p => p.Status).ToListAsync())
                .Should().Equal(FoodProposalStatus.Pending, FoodProposalStatus.Rejected);
            (await db.AuditLogEntries.AnyAsync()).Should().BeFalse();
            (await service.LinkVerifiedWordingsAsync()).ProposalsWithdrawn.Should().Be(2, "a second apply withdraws them again");
        }

        [Fact]
        public async Task A_withdrawn_proposal_stays_withdrawn_when_its_ingredient_cannot_come_back()
        {
            using var db = NewContext();
            var extract = CuratedFood("Vanilla Extract");
            var vanilla = Uncurated("vanilla");
            db.Ingredients.AddRange(extract, vanilla);
            await db.SaveChangesAsync();
            db.IngredientWordings.Add(Wording("vanilla", "food", extract));
            var pending = OpenLink(vanilla);
            db.FoodCatalogProposals.Add(pending);
            await db.SaveChangesAsync();
            var service = new LineRelinkService(db, new NoUser());
            var started = DateTime.UtcNow;
            await service.LinkVerifiedWordingsAsync(apply: true);
            db.Ingredients.Add(Uncurated("vanilla"));
            await db.SaveChangesAsync();

            var undone = await service.UndoAsync(started, apply: true);

            (undone.FoodsRestored, undone.ProposalsRestored, undone.Skipped).Should().Be((0, 0, 2));
            (await db.Ingredients.IgnoreQueryFilters().SingleAsync(i => i.Id == vanilla.Id)).IsDeleted.Should().BeTrue();
            (await db.FoodCatalogProposals.IgnoreQueryFilters().SingleAsync(p => p.Id == pending.Id)).IsDeleted.Should().BeTrue();
            (await db.AuditLogEntries.CountAsync(a => a.ChangeType == LineRelinkService.ProposalWithdrawChangeType)).Should().Be(1,
                "the entry stays, so a later undo can bring the proposal back with its ingredient");
        }

        [Fact]
        public async Task A_dry_run_counts_the_sources_and_proposals_every_batch_of_the_apply_retires_and_withdraws()
        {
            using var db = NewContext();
            var extract = CuratedFood("Vanilla Extract");
            var milk = CuratedFood("Milk");
            var vanilla = Uncurated("vanilla");
            var sweet = Uncurated("sweet milk");
            db.Ingredients.AddRange(extract, milk, vanilla, sweet);
            await db.SaveChangesAsync();
            db.IngredientWordings.AddRange(Wording("vanilla", "food", extract), Wording("sweet milk", "food", milk));
            db.Recipes.AddRange(
                Recipe("1", DateTime.UtcNow, (vanilla, "1 tsp vanilla", null)),
                Recipe("2", DateTime.UtcNow, (sweet, "1 cup sweet milk", null)));
            db.FoodCatalogProposals.AddRange(OpenLink(vanilla), OpenLink(sweet));
            await db.SaveChangesAsync();
            var service = new LineRelinkService(db, new NoUser());

            var dry = await service.LinkVerifiedWordingsAsync(maxLines: 1);

            (dry.Remaining, dry.SourcesRetired, dry.ProposalsWithdrawn).Should().Be((2, 2, 2));
            var first = await service.LinkVerifiedWordingsAsync(apply: true, maxLines: 1);
            var second = await service.LinkVerifiedWordingsAsync(apply: true, maxLines: 1);
            (first.SourcesRetired + second.SourcesRetired, first.ProposalsWithdrawn + second.ProposalsWithdrawn).Should().Be((2, 2));
        }

        [Fact]
        public async Task Two_lines_landing_on_the_same_food_take_their_positions_in_food_order_whatever_order_they_were_stored_in()
        {
            using var db = NewContext();
            var milk = CuratedFood("Milk");
            var sweet = Uncurated("sweet milk");
            var newMilk = Uncurated("new milk");
            db.Ingredients.AddRange(milk, newMilk, sweet);
            await db.SaveChangesAsync();
            db.IngredientWordings.AddRange(Wording("sweet milk", "food", milk), Wording("new milk", "food", milk));
            db.Recipes.Add(new RecipeEntity
            {
                Name = "112625",
                ScrapedAtUtc = DateTime.UtcNow,
                RecipeIngredients = new List<RecipeIngredientEntity>
                {
                    new() { IngredientId = sweet.Id, SortOrder = 0, Quantity = 1, MeasurementId = 1, RawLine = "1 cup sweet milk" },
                    new() { IngredientId = newMilk.Id, SortOrder = 0, Quantity = 2, MeasurementId = 1, RawLine = "2 cups new milk" },
                },
            });
            await db.SaveChangesAsync();

            await new LineRelinkService(db, new NoUser()).LinkVerifiedWordingsAsync(apply: true);

            (await db.RecipeIngredients.AsNoTracking().OrderBy(ri => ri.SortOrder).Select(ri => ri.RawLine).ToListAsync())
                .Should().Equal("2 cups new milk", "1 cup sweet milk");
        }

        [Fact]
        public async Task A_pending_pick_the_table_judged_otherwise_is_withdrawn_with_a_journal_entry_and_the_undo_opens_it_again()
        {
            using var db = NewContext();
            var extract = CuratedFood("Vanilla Extract");
            var soymilk = CuratedFood("SILK Very Vanilla soymilk");
            var vanilla = Uncurated("vanilla");
            var buttermilk = Uncurated("sweet milk");
            db.Ingredients.AddRange(extract, soymilk, vanilla, buttermilk);
            await db.SaveChangesAsync();
            db.IngredientWordings.AddRange(Wording("vanilla", "food", extract), Wording("sweet milk", "unclear"));
            db.Recipes.Add(Recipe("Written by a member", null, (vanilla, "1 tsp vanilla", null), (buttermilk, "1 cup sweet milk", null)));
            var wrong = OpenLink(vanilla);
            wrong.CurrentValue = "vanilla";
            wrong.ProposedValue = soymilk.Id.ToString();
            var unclear = OpenLink(buttermilk);
            unclear.CurrentValue = "sweet milk";
            unclear.ProposedValue = soymilk.Id.ToString();
            var agreeing = OpenLink(vanilla);
            agreeing.CurrentValue = "Vanilla";
            agreeing.ProposedValue = extract.Id.ToString();
            db.FoodCatalogProposals.AddRange(wrong, unclear, agreeing);
            await db.SaveChangesAsync();
            var service = new LineRelinkService(db, new NoUser());
            var started = DateTime.UtcNow;

            (await service.LinkVerifiedWordingsAsync()).PicksWithdrawn.Should().Be(2);
            (await db.FoodCatalogProposals.CountAsync()).Should().Be(3, "a dry run writes nothing");
            var applied = await service.LinkVerifiedWordingsAsync(apply: true);

            applied.Should().Match<VerifiedWordingLinkResult>(r => r.SourcesKept == 1 && r.PicksWithdrawn == 2);
            (await db.FoodCatalogProposals.Select(p => p.Id).ToListAsync()).Should().Equal(agreeing.Id);
            (await db.AuditLogEntries.Where(a => a.ChangeType == LineRelinkService.PickWithdrawChangeType).Select(a => a.EntityId).ToListAsync())
                .Should().BeEquivalentTo(new[] { wrong.Id, unclear.Id });

            (await service.UndoAsync(started, apply: true)).PicksRestored.Should().Be(2);
            (await db.FoodCatalogProposals.CountAsync()).Should().Be(3);
        }

        [Fact]
        public async Task A_rename_is_journaled_and_an_undo_gives_the_name_back()
        {
            using var db = NewContext();
            var seed = CuratedFood("Black Mustard Seed");
            db.Ingredients.Add(seed);
            await db.SaveChangesAsync();
            var service = new LineRelinkService(db, new NoUser());
            var started = DateTime.UtcNow;

            var renamed = await service.RenameAsync(seed.Id, "Ground Mustard");

            renamed!.Should().Match<FoodRenameResult>(r => r.Renamed && r.Name == "Ground Mustard" && r.HeldByIngredientId == null);
            (await db.Ingredients.SingleAsync()).Should().Match<IngredientEntity>(i => i.Name == "Ground Mustard" && i.NameNormalized == "Ground Mustard");
            (await service.RenameAsync(seed.Id, "Ground Mustard")).Should().BeNull("the name is unchanged");
            (await service.RenameAsync(seed.Id, " ")).Should().BeNull();

            await service.UndoAsync(started, apply: true);

            (await db.Ingredients.SingleAsync()).Should().Match<IngredientEntity>(i => i.Name == "Black Mustard Seed" && i.NameNormalized == "Black Mustard Seed");
            (await db.AuditLogEntries.AnyAsync()).Should().BeFalse();
        }

        [Fact]
        public async Task A_rename_moves_a_deleted_holder_out_of_the_way_and_refuses_a_live_one()
        {
            using var db = NewContext();
            var seed = CuratedFood("Black Mustard Seed");
            var oil = CuratedFood("Toasted Sesame Oil");
            var deleted = new IngredientEntity { Name = "Ground Mustard", CurationStatusId = (long)CurationStatusEnum.NonCurated, IsDeleted = true };
            var live = Uncurated("sesame oil");
            db.Ingredients.AddRange(seed, oil, deleted, live);
            await db.SaveChangesAsync();
            db.Recipes.Add(Recipe("Scraped", DateTime.UtcNow, (live, "1 tbsp sesame oil", null), (live, "sesame oil", null)));
            await db.SaveChangesAsync();
            var service = new LineRelinkService(db, new NoUser());

            var held = await service.RenameAsync(oil.Id, "Sesame Oil");
            var renamed = await service.RenameAsync(seed.Id, "Ground Mustard");

            held!.Should().Match<FoodRenameResult>(r => !r.Renamed && r.HeldByIngredientId == live.Id && r.HeldByLines == 2 && r.Name == "Toasted Sesame Oil");
            (await db.Ingredients.SingleAsync(i => i.Id == oil.Id)).Name.Should().Be("Toasted Sesame Oil");
            (await db.AuditLogEntries.CountAsync(a => a.EntityId == oil.Id)).Should().Be(0);
            renamed!.Renamed.Should().BeTrue();
            (await db.Ingredients.IgnoreQueryFilters().SingleAsync(i => i.Id == deleted.Id)).Name.Should().Be($"Ground Mustard (deleted #{deleted.Id})");
        }

        [Fact]
        public async Task A_rename_takes_the_name_off_other_foods_aliases_and_undo_gives_them_back()
        {
            using var db = NewContext();
            var seed = CuratedFood("Black Mustard Seed");
            var mustard = CuratedFood("Mustard");
            mustard.Aliases = new List<IngredientAliasEntity> { new() { AliasName = "ground mustard" } };
            db.Ingredients.AddRange(seed, mustard);
            await db.SaveChangesAsync();
            var service = new LineRelinkService(db, new NoUser());
            var started = DateTime.UtcNow;

            var renamed = await service.RenameAsync(seed.Id, "Ground Mustard");

            renamed!.AliasesRemoved.Should().Be(1);
            (await db.IngredientAliases.AnyAsync()).Should().BeFalse();

            await service.UndoAsync(started, apply: true);

            (await db.IngredientAliases.SingleAsync()).Should().Match<IngredientAliasEntity>(a => a.IngredientId == mustard.Id && a.AliasName == "ground mustard");
        }
    }
}

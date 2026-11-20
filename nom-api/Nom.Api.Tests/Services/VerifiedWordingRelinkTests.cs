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
                && r.Sources == 2 && r.Remaining == 2 && r.Moved == 0 && r.SourcesRetired == 1 && r.SourcesKept == 1 && r.ProposalsWithdrawn == 1);
            plan.Samples.Select(m => (m.FromName, m.Wording, m.ToName, m.Lines)).Should().BeEquivalentTo(new[]
            {
                ("vanilla", "vanilla", "Vanilla Extract", 1),
                ("lemon rind", "lemon rind", "Lemon Peel", 1),
            });
            (await db.Ingredients.CountAsync()).Should().Be(7);
            (await db.FoodCatalogProposals.CountAsync()).Should().Be(1);
            (await db.AuditLogEntries.AnyAsync()).Should().BeFalse();
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
            (await db.AuditLogEntries.SingleAsync()).Should().Match<AuditLogEntryEntity>(a =>
                a.ChangeType == LineRelinkService.RetireChangeType && a.EntityId == vanilla.Id && a.NewValue == $"{extract.Id}@0");

            var undone = await service.UndoAsync(started);

            undone.FoodsRestored.Should().Be(1);
            (await db.Ingredients.SingleAsync(i => i.Id == vanilla.Id)).Name.Should().Be("vanilla");
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

            await service.UndoAsync(started);

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

            await service.UndoAsync(started);

            (await db.IngredientAliases.SingleAsync()).Should().Match<IngredientAliasEntity>(a => a.IngredientId == mustard.Id && a.AliasName == "ground mustard");
        }
    }
}

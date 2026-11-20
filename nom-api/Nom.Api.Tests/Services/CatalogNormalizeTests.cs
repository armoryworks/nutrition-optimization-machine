using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Nom.Data;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;
using Nom.Orch.Services;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// Normalising replaces the aliases that still carry a quantity and no others. Alias rows have
    /// a composite key and all share Id 0, so a delete by Id once took every alias in the table.
    /// </summary>
    public class CatalogNormalizeTests
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

        private static async Task<IngredientEntity> FoodAsync(ApplicationDbContext db, string name)
        {
            var food = new IngredientEntity { Name = name, NameNormalized = name, CurationStatusId = 9003 };
            db.Ingredients.Add(food);
            await db.SaveChangesAsync();
            return food;
        }

        private static IngredientAliasEntity Alias(IngredientEntity food, string wording, string? context = null, bool deleted = false) =>
            new() { IngredientId = food.Id, AliasName = wording, SourceContext = context, IsDeleted = deleted };

        [Fact]
        public async Task Normalising_removes_only_the_residue_aliases()
        {
            using var db = NewContext();
            var vanilla = await FoodAsync(db, "Vanilla Extract");
            db.IngredientAliases.AddRange(
                Alias(vanilla, "1 teaspoon vanilla extract"),
                Alias(vanilla, "2 teaspoons vanilla essence"),
                Alias(vanilla, "vanilla"),
                Alias(vanilla, "Vanilla extract", IngredientLinkService.UsdaDescriptionContext),
                Alias(vanilla, "pure vanilla extract", "link:deterministic:name-match"));
            await db.SaveChangesAsync();
            (await db.IngredientAliases.Select(a => a.Id).Distinct().ToListAsync()).Should().Equal(0L);
            db.ChangeTracker.Clear();

            var result = await new CatalogCleanupService(db, new NoUser()).NormalizeAsync(apply: true);

            result.Should().BeEquivalentTo(new { Applied = true, AliasesRemoved = 2, AliasesAdded = 1 });
            var left = await db.IngredientAliases.IgnoreQueryFilters().AsNoTracking().ToListAsync();
            left.Select(a => (a.AliasName, a.SourceContext)).Should().BeEquivalentTo(new[]
            {
                ("vanilla", (string?)null),
                ("Vanilla extract", IngredientLinkService.UsdaDescriptionContext),
                ("pure vanilla extract", "link:deterministic:name-match"),
                ("vanilla essence", null),
            });
        }

        [Fact]
        public async Task A_dry_run_counts_the_residue_and_changes_nothing()
        {
            using var db = NewContext();
            var vanilla = await FoodAsync(db, "Vanilla Extract");
            db.IngredientAliases.AddRange(Alias(vanilla, "2 teaspoons vanilla essence"), Alias(vanilla, "vanilla"));
            await db.SaveChangesAsync();

            var result = await new CatalogCleanupService(db, new NoUser()).NormalizeAsync(apply: false);

            result.Should().BeEquivalentTo(new { Applied = false, AliasesRemoved = 1, AliasesAdded = 1 });
            (await db.IngredientAliases.Select(a => a.AliasName).ToListAsync()).Should().BeEquivalentTo("2 teaspoons vanilla essence", "vanilla");
        }

        [Fact]
        public async Task The_normalised_alias_keeps_the_context_of_the_one_it_replaces()
        {
            using var db = NewContext();
            var eggs = await FoodAsync(db, "Eggs");
            var onion = await FoodAsync(db, "Yellow Onion");
            db.IngredientAliases.AddRange(
                Alias(eggs, "2 large eggs, beaten", "link:deterministic:staple"),
                Alias(eggs, "3 large eggs"),
                Alias(eggs, "4 large eggs", "admin:link:ai:fake"),
                Alias(eggs, "6 large eggs", "link:ai:fake"),
                Alias(onion, "1 large yellow onion", "link:ai:fake"));
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var result = await new CatalogCleanupService(db, new NoUser()).NormalizeAsync(apply: true);

            result.Should().BeEquivalentTo(new { AliasesRemoved = 5, AliasesAdded = 3 });
            (await db.IngredientAliases.AsNoTracking().Select(a => new { a.IngredientId, a.AliasName, a.SourceContext }).ToListAsync()).Should().BeEquivalentTo(new[]
            {
                new { IngredientId = eggs.Id, AliasName = "large eggs, beaten", SourceContext = (string?)"link:deterministic:staple" },
                new { IngredientId = eggs.Id, AliasName = "large eggs", SourceContext = (string?)"admin:link:ai:fake" },
                new { IngredientId = onion.Id, AliasName = "large yellow onion", SourceContext = (string?)"link:ai:fake" },
            }, "of the wordings that collapse into one, the trusted context wins whatever order they come in");
        }

        [Fact]
        public async Task A_usda_description_and_a_retired_name_are_not_wordings_and_stay_as_they_are()
        {
            using var db = NewContext();
            var soy = await FoodAsync(db, "Soy Sauce");
            db.IngredientAliases.AddRange(
                Alias(soy, "Soy sauce made from soy and wheat (shoyu)", IngredientLinkService.UsdaDescriptionContext),
                Alias(soy, "Soy Sauce (Kikkoman)", "retired:279901"));
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var result = await new CatalogCleanupService(db, new NoUser()).NormalizeAsync(apply: true);

            result.Should().BeEquivalentTo(new { AliasesRemoved = 0, AliasesAdded = 0 });
            (await db.IngredientAliases.Select(a => a.AliasName).ToListAsync()).Should().BeEquivalentTo(
                "Soy sauce made from soy and wheat (shoyu)", "Soy Sauce (Kikkoman)");
        }

        [Fact]
        public async Task A_wording_that_was_removed_from_the_food_is_not_put_back()
        {
            using var db = NewContext();
            var vanilla = await FoodAsync(db, "Vanilla Extract");
            db.IngredientAliases.AddRange(
                Alias(vanilla, "2 teaspoons vanilla essence"),
                Alias(vanilla, "vanilla essence", deleted: true));
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var result = await new CatalogCleanupService(db, new NoUser()).NormalizeAsync(apply: true);

            result.Should().BeEquivalentTo(new { AliasesRemoved = 1, AliasesAdded = 0 });
            var left = await db.IngredientAliases.IgnoreQueryFilters().AsNoTracking().SingleAsync();
            left.Should().Match<IngredientAliasEntity>(a => a.AliasName == "vanilla essence" && a.IsDeleted);
        }
    }
}

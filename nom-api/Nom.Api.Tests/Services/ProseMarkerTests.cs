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
using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// The step-marker cleanup takes each of the marker forms seen in scraped methods out of the
    /// step text and changes nothing else, removes a step that held only a marker, leaves steps a
    /// person edited, journals every step with its whole old text, and the undo puts them back
    /// unless the step changed since.
    /// </summary>
    public class ProseMarkerTests
    {
        private const long Admin = 77;

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

        [Theory]
        [InlineData("1. Heat the oven to 350 degrees F.", "Heat the oven to 350 degrees F.")]
        [InlineData("Step 2: Mix the flour and the sugar.", "Mix the flour and the sugar.")]
        [InlineData("2In a bowl, whisk the eggs.", "In a bowl, whisk the eggs.")]
        [InlineData("Simmer for 2 hours.3.Stir in the cream.", "Simmer for 2 hours. Stir in the cream.")]
        [InlineData("Toss to mix. 3.", "Toss to mix.")]
        [InlineData("4) Fold in the berries.", "Fold in the berries.")]
        [InlineData("Step 5:", "")]
        public void Each_marker_form_is_taken_out_and_nothing_else_changes(string step, string expected)
        {
            ProseQuantities.HasStepMarker(step).Should().BeTrue();
            ProseQuantities.StripStepMarkers(step).Should().Be(expected);
            ProseQuantities.HasStepMarker(expected).Should().BeFalse();
        }

        [Theory]
        [InlineData("Bake for 25 minutes at 350 degrees F.")]
        [InlineData("Cut into 1-inch pieces and add 2 cups of stock.")]
        [InlineData("Cook 3 to 4 minutes per side.")]
        public void A_step_without_a_marker_is_left_as_it_is(string step)
        {
            ProseQuantities.StripStepMarkers(step).Should().Be(step);
        }

        private static async Task<long> SeedAsync(ApplicationDbContext db, bool scraped = true, bool withheld = false, bool quarantined = false)
        {
            var recipe = new RecipeEntity
            {
                Name = "Berry cake",
                ScrapedAtUtc = scraped ? new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc) : null,
                ProseWithheld = withheld,
                ContainsSourceProse = quarantined,
                CurationStatusId = (long)CurationStatusEnum.Curated,
                RecipeSteps = new[]
                {
                    "1. Heat the oven to 350 degrees F.",
                    "Toss to mix. 3.",
                    "Step 3:",
                    "Fold in the berries.",
                    "5. Bake until golden.",
                }.Select((text, i) => new RecipeStepEntity
                {
                    StepNumber = i + 1, Description = text, Summary = text,
                    LastModifiedByPersonId = i == 4 ? Admin : SystemConstants.SystemPersonId,
                }).ToList(),
            };
            db.Recipes.Add(recipe);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            return recipe.Id;
        }

        [Fact]
        public async Task The_dry_run_counts_and_writes_nothing()
        {
            await using var db = NewContext();
            await SeedAsync(db);
            await SeedAsync(db, scraped: false);

            var result = await new ProseMarkerService(db, new NoUser()).StripAsync();

            result.Applied.Should().BeFalse();
            result.StepsWithMarker.Should().Be(4, "steps of recipes NOM did not scrape are the author's own");
            result.Recipes.Should().Be(1);
            result.Stripped.Should().Be(3);
            result.Removed.Should().Be(1);
            result.SkippedEditedByPerson.Should().Be(1);
            result.Samples.Select(s => s.After).Should().Equal("Heat the oven to 350 degrees F.", "Toss to mix.", "");
            (await db.AuditLogEntries.CountAsync()).Should().Be(0);
            (await db.RecipeSteps.CountAsync(s => s.Description == "Step 3:")).Should().Be(2);
        }

        [Fact]
        public async Task Apply_strips_and_journals_each_step_and_the_undo_puts_back_the_unchanged_ones()
        {
            await using var db = NewContext();
            var recipeId = await SeedAsync(db);
            var service = new ProseMarkerService(db, new NoUser());
            var before = DateTime.UtcNow.AddSeconds(-1);

            var result = await service.StripAsync(apply: true);
            db.ChangeTracker.Clear();

            result.Stripped.Should().Be(3);
            result.Remaining.Should().Be(0);
            var live = await db.RecipeSteps.Where(s => s.RecipeId == recipeId).OrderBy(s => s.StepNumber).Select(s => s.Description).ToListAsync();
            live.Should().Equal("Heat the oven to 350 degrees F.", "Toss to mix.", "Fold in the berries.", "5. Bake until golden.");
            var journal = await db.AuditLogEntries.Where(a => a.ChangeType == ProseMarkerService.StripChangeType).ToListAsync();
            journal.Should().HaveCount(3);
            journal.Single(a => a.PropertyName == "RecipeStep:3").Should().BeEquivalentTo(new { EntityType = "Recipe", EntityId = recipeId, OldValue = "Step 3:", NewValue = "" });
            (await db.RecipeSteps.SingleAsync(s => s.RecipeId == recipeId && s.StepNumber == 1)).LastModifiedByPersonId.Should().Be(SystemConstants.SystemPersonId,
                "a system edit must not make the step look edited by a person to the prose re-check");
            (await service.StripAsync()).Stripped.Should().Be(0, "a repeat finds nothing left");

            var changed = await db.RecipeSteps.SingleAsync(s => s.RecipeId == recipeId && s.StepNumber == 2);
            changed.Description = "Toss well.";
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var dryUndo = await service.UndoAsync(before);
            dryUndo.Should().BeEquivalentTo(new { Applied = false, Restored = 2, Skipped = 1, Remaining = 2 });

            var undo = await service.UndoAsync(before, apply: true);
            db.ChangeTracker.Clear();

            undo.Should().BeEquivalentTo(new { Applied = true, Restored = 2, Skipped = 1, Remaining = 0 });
            live = await db.RecipeSteps.Where(s => s.RecipeId == recipeId).OrderBy(s => s.StepNumber).Select(s => s.Description).ToListAsync();
            live.Should().Equal("1. Heat the oven to 350 degrees F.", "Toss well.", "Step 3:", "Fold in the berries.", "5. Bake until golden.");
            (await service.UndoAsync(before)).Restored.Should().Be(0, "each step put back is journaled, so a repeat finds nothing left");
        }

        [Fact]
        public async Task The_samples_never_serve_the_text_of_a_withheld_or_quarantined_recipe_though_its_steps_are_stripped()
        {
            await using var db = NewContext();
            var withheld = await SeedAsync(db, withheld: true);
            var quarantined = await SeedAsync(db, quarantined: true);
            var shown = await SeedAsync(db);

            var result = await new ProseMarkerService(db, new NoUser()).StripAsync(sampleSize: 1000);

            result.Stripped.Should().Be(9);
            result.RecipesChanged.Should().Be(3);
            result.Samples.Should().HaveCount(3).And.OnlyContain(s => s.RecipeId == shown);
            result.Samples.Select(s => s.RecipeId).Should().NotContain(new[] { withheld, quarantined });
        }

        [Fact]
        public async Task Apply_stops_at_the_recipe_limit()
        {
            await using var db = NewContext();
            await SeedAsync(db);
            await SeedAsync(db);

            var result = await new ProseMarkerService(db, new NoUser()).StripAsync(apply: true, maxRecipes: 1);

            result.RecipesChanged.Should().Be(1);
            result.Stripped.Should().Be(3);
            result.Remaining.Should().Be(3);
        }
    }
}

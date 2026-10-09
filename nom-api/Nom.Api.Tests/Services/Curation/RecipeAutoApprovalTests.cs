using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nom.Data;
using Nom.Data.Recipe;
using Nom.Data.Reference;
using Nom.Orch.Models.Curation;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Services.Curation
{
    /// <summary>
    /// R1 approves vetted public-domain recipes that publish no source image; R2 approves scraped
    /// recipes of unknown license once their prose is rewritten and they carry an image of our own.
    /// Both need every ingredient curated — the same gate an admin approval passes.
    /// </summary>
    public class RecipeAutoApprovalTests
    {
        private static RecipeEntity PublicDomain(long id = 10, bool ingredientCurated = true) => new()
        {
            Id = id,
            Name = "Shrewsbury Cakes",
            Visibility = RecipeVisibilityEnum.Public,
            CurationStatusId = (long)CurationStatusEnum.NonCurated,
            LicenseStatus = RecipeLicenseStatus.PublicDomain,
            ScrapedAtUtc = DateTime.UtcNow,
            AuthorId = SystemConstants.SystemPersonId,
            RecipeIngredients = new List<RecipeIngredientEntity>
            {
                Line(id * 10 + 1, "1 pound flour", 1m, ingredientCurated),
                Line(id * 10 + 2, "2 eggs", 2m, true),
            },
            RecipeSteps = new List<RecipeStepEntity>
            {
                new() { StepNumber = 1, Summary = "Rub", Description = "Rub the butter into the flour." },
                new() { StepNumber = 2, Summary = "Bake", Description = "Bake in a quick oven for a quarter of an hour." },
            },
        };

        private static RecipeEntity RewrittenScrape(long id = 20)
        {
            var recipe = PublicDomain(id);
            recipe.LicenseStatus = RecipeLicenseStatus.Unknown;
            recipe.SourceUrl = "https://example-recipes.com/cakes";
            recipe.SourceImageUrl = "https://example-recipes.com/img/cakes.jpg";
            recipe.Image = "https://upload.wikimedia.org/cakes.jpg";
            return recipe;
        }

        private static RecipeIngredientEntity Line(long ingredientId, string raw, decimal quantity, bool curated) => new()
        {
            IngredientId = ingredientId,
            Ingredient = new IngredientEntity
            {
                Id = ingredientId,
                Name = raw.Split(' ').Last(),
                CurationStatusId = curated ? (long)CurationStatusEnum.Curated : (long)CurationStatusEnum.PendingCuration,
            },
            RawLine = raw,
            Quantity = quantity,
            MeasurementId = 1,
        };

        [Fact]
        public void R1_public_domain_recipe_is_eligible()
        {
            var decision = RecipeAutoApprovalPolicy.Evaluate(PublicDomain());
            decision.Eligible.Should().BeTrue();
            decision.Rule.Should().Be(RecipeAutoApprovalPolicy.PublicDomainRule);
        }

        [Fact]
        public void R1_public_domain_recipe_with_a_non_source_image_is_eligible()
        {
            var recipe = PublicDomain();
            recipe.Image = "/images/recipes/10.webp";
            RecipeAutoApprovalPolicy.Evaluate(recipe).Eligible.Should().BeTrue();
        }

        [Fact]
        public void R2_rewritten_scrape_with_own_image_is_eligible()
        {
            var decision = RecipeAutoApprovalPolicy.Evaluate(RewrittenScrape());
            decision.Eligible.Should().BeTrue();
            decision.Rule.Should().Be(RecipeAutoApprovalPolicy.RewrittenScrapeRule);
        }

        public static IEnumerable<object[]> Ineligible()
        {
            yield return Case("vetting issues", r => r.VettingIssues = "Only 1 instruction step(s) — likely an incomplete extraction.");
            yield return Case("source prose", r => r.ContainsSourceProse = true);
            yield return Case("pending status", r => r.CurationStatusId = (long)CurationStatusEnum.PendingCuration);
            yield return Case("requires revision", r => r.CurationStatusId = (long)CurationStatusEnum.RequiresRevision);
            yield return Case("already approved", r => r.CurationStatusId = (long)CurationStatusEnum.Curated);
            yield return Case("household visibility", r => r.Visibility = RecipeVisibilityEnum.Household);
            yield return Case("private visibility", r => r.Visibility = RecipeVisibilityEnum.Private);
            yield return Case("curator already reviewed", r => r.DateCurationCompleted = DateTime.UtcNow);
            yield return Case("user-submitted license", r => r.LicenseStatus = RecipeLicenseStatus.UserSubmitted);
            yield return Case("no license", r => r.LicenseStatus = null);
            yield return Case("all rights reserved", r => r.LicenseStatus = RecipeLicenseStatus.AllRightsReserved);
            yield return Case("published image is the source image", r =>
            {
                r.SourceImageUrl = "https://example-recipes.com/img/cakes.jpg";
                r.Image = "https://example-recipes.com/img/cakes.jpg";
            });
            yield return Case("image hotlinked from the source site", r =>
            {
                r.SourceUrl = "https://www.example-recipes.com/cakes";
                r.Image = "https://example-recipes.com/other.jpg";
            });
            yield return Case("an uncurated ingredient", r => r.RecipeIngredients!.First().Ingredient.CurationStatusId = (long)CurationStatusEnum.PendingCuration);
            yield return Case("deleted", r => r.IsDeleted = true);
        }

        private static object[] Case(string name, Action<RecipeEntity> spoil) => new object[] { name, spoil };

        [Theory]
        [MemberData(nameof(Ineligible))]
        public void R1_rejects(string because, Action<RecipeEntity> spoil)
        {
            var recipe = PublicDomain();
            spoil(recipe);
            RecipeAutoApprovalPolicy.Evaluate(recipe).Eligible.Should().BeFalse(because);
        }

        [Theory]
        [MemberData(nameof(Ineligible))]
        public void R2_rejects(string because, Action<RecipeEntity> spoil)
        {
            var recipe = RewrittenScrape();
            spoil(recipe);
            if (recipe.LicenseStatus == RecipeLicenseStatus.PublicDomain) return;
            RecipeAutoApprovalPolicy.Evaluate(recipe).Eligible.Should().BeFalse(because);
        }

        [Fact]
        public void R2_needs_an_image_of_our_own()
        {
            var recipe = RewrittenScrape();
            recipe.Image = null;
            RecipeAutoApprovalPolicy.Evaluate(recipe).Eligible.Should().BeFalse();

            recipe.Image = recipe.SourceImageUrl;
            RecipeAutoApprovalPolicy.Evaluate(recipe).Eligible.Should().BeFalse();
        }

        [Fact]
        public void R2_needs_the_prose_rewritten()
        {
            var recipe = RewrittenScrape();
            recipe.ContainsSourceProse = true;
            RecipeAutoApprovalPolicy.Evaluate(recipe).Reason.Should().Contain("prose");
        }

        [Fact]
        public void Unknown_license_that_was_not_scraped_is_not_eligible()
        {
            var recipe = RewrittenScrape();
            recipe.ScrapedAtUtc = null;
            RecipeAutoApprovalPolicy.Evaluate(recipe).Eligible.Should().BeFalse();
        }

        private static async Task<ApplicationDbContext> SeedAsync(params RecipeEntity[] recipes)
        {
            var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            db.References.AddRange(
                new ReferenceEntity { Id = 9100, Name = "Recipe" },
                new ReferenceEntity { Id = 9199, Name = "Approval" },
                new ReferenceEntity { Id = (long)CurationStatusEnum.PendingCuration, Name = "Pending Curation" },
                new ReferenceEntity { Id = (long)CurationStatusEnum.Curated, Name = "Approved" });
            db.Recipes.AddRange(recipes);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            return db;
        }

        private static RecipeAutoCurationService Service(ApplicationDbContext db) =>
            new(db, new RecipeVettingService(), NullLogger<RecipeAutoCurationService>.Instance);

        [Fact]
        public async Task Sweep_approves_eligible_recipes_as_the_system_person_with_a_note()
        {
            using var db = await SeedAsync(PublicDomain(10), PublicDomain(11, ingredientCurated: false), RewrittenScrape(12));

            var result = await Service(db).AutoApproveBatchAsync(0, 50);

            result.Approved.Should().Be(2);
            var statuses = await db.Recipes.OrderBy(r => r.Id).Select(r => r.CurationStatusId).ToListAsync();
            statuses.Should().Equal((long)CurationStatusEnum.Curated, (long)CurationStatusEnum.NonCurated, (long)CurationStatusEnum.Curated);
            var approved = await db.Recipes.SingleAsync(r => r.Id == 10);
            approved.DateCurationCompleted.Should().NotBeNull();
            approved.LastModifiedByPersonId.Should().Be(SystemConstants.SystemPersonId);
            var notes = await db.CurationFeedbacks.OrderBy(f => f.EntityId).ToListAsync();
            notes.Select(n => n.EntityId).Should().Equal(10, 12);
            notes.Should().OnlyContain(n => n.AdminId == SystemConstants.SystemPersonId && n.FeedbackTypeId == 9199 && n.EntityTypeId == 9100);
            notes[0].FeedbackNotes.Should().StartWith("auto-approved: R1");
            notes[1].FeedbackNotes.Should().StartWith("auto-approved: R2");
        }

        [Fact]
        public async Task Sweep_picks_a_recipe_up_once_its_last_ingredient_is_curated()
        {
            using var db = await SeedAsync(PublicDomain(10, ingredientCurated: false));
            (await Service(db).AutoApproveBatchAsync(0, 50)).Approved.Should().Be(0);

            var ingredient = await db.Ingredients.SingleAsync(i => i.CurationStatusId == (long)CurationStatusEnum.PendingCuration);
            ingredient.CurationStatusId = (long)CurationStatusEnum.Curated;
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            (await Service(db).AutoApproveBatchAsync(0, 50)).Approved.Should().Be(1);
        }

        [Fact]
        public async Task Sweep_skips_recipes_that_fail_vetting_today()
        {
            var thin = PublicDomain(10);
            thin.RecipeSteps!.Remove(thin.RecipeSteps.Last());
            using var db = await SeedAsync(thin);

            (await Service(db).AutoApproveBatchAsync(0, 50)).Approved.Should().Be(0);
        }

        [Fact]
        public async Task Admin_approval_refuses_a_recipe_with_source_prose()
        {
            var recipe = PublicDomain(10);
            recipe.ContainsSourceProse = true;
            using var db = await SeedAsync(recipe);
            var curation = new CurationOrchestrationService(db, NullLogger<CurationOrchestrationService>.Instance);

            var act = () => curation.ApproveAsync(new CurationDecisionRequest { EntityType = "Recipe", EntityId = 10 }, 1);

            (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*verbatim prose*");
            (await db.Recipes.SingleAsync()).CurationStatusId.Should().Be((long)CurationStatusEnum.NonCurated);
        }

        [Fact]
        public async Task Admin_approval_still_refuses_uncurated_ingredients()
        {
            using var db = await SeedAsync(PublicDomain(10, ingredientCurated: false));
            var curation = new CurationOrchestrationService(db, NullLogger<CurationOrchestrationService>.Instance);

            var act = () => curation.ApproveAsync(new CurationDecisionRequest { EntityType = "Recipe", EntityId = 10 }, 1);

            (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*not curated: flour*");
        }

        [Fact]
        public async Task Requesting_a_revision_records_the_seeded_revision_request_feedback()
        {
            using var db = await SeedAsync(PublicDomain(10));
            db.References.Add(new ReferenceEntity { Id = 9202, Name = "Revision Request" });
            await db.SaveChangesAsync();
            var curation = new CurationOrchestrationService(db, NullLogger<CurationOrchestrationService>.Instance);

            await curation.RequestRevisionAsync(new CurationDecisionRequest { EntityType = "Recipe", EntityId = 10, DecisionNotes = "Steps are out of order." }, 1);

            (await db.Recipes.SingleAsync()).CurationStatusId.Should().Be((long)CurationStatusEnum.RequiresRevision);
            (await db.CurationFeedbacks.SingleAsync()).FeedbackTypeId.Should().Be(9202);
        }
    }
}

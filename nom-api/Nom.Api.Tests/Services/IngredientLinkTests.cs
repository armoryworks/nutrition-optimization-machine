using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Nom.Data;
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
    /// Catalog ingredients get linked to USDA foods through admin-reviewed proposals: exact name
    /// matches are proposed deterministically, ambiguous ones by the model choosing among real USDA
    /// rows, and approving a link merges or copies USDA data — never a model-authored number.
    /// </summary>
    public class IngredientLinkTests
    {
        private sealed class FakeMatcher : IIngredientLinkMatcher
        {
            public Func<LinkQuestion, LinkAnswer> Answer { get; set; } = q => new LinkAnswer(q.IngredientId, null, 0m);
            public List<string> Asked { get; } = new();
            public bool IsConfigured => true;
            public string ModelName => "fake:1b";

            public Task<IReadOnlyList<LinkAnswer>> ChooseAsync(IReadOnlyList<LinkQuestion> questions, CancellationToken cancellationToken = default)
            {
                Asked.AddRange(questions.Select(q => q.Name));
                return Task.FromResult<IReadOnlyList<LinkAnswer>>(questions.Select(Answer).ToList());
            }
        }

        private sealed class FakeCleanup : ICatalogCleanupService
        {
            public bool Merges { get; set; } = true;
            public List<(long Source, long Target)> Merged { get; } = new();
            public Task<CatalogCleanupPreview> PreviewAsync(int sampleSize = 50) => throw new NotSupportedException();
            public Task<CatalogCleanupResult> ApplyAsync(int maxActions = 500) => throw new NotSupportedException();

            public Task<bool> MergeIntoAsync(long sourceId, long targetId, long? personId, bool ignoreProposals = false)
            {
                if (Merges) Merged.Add((sourceId, targetId));
                return Task.FromResult(Merges);
            }
        }

        private sealed class NoAudit : IFoodCatalogAuditService
        {
            public Task<FoodCatalogAuditResult> AuditAsync(string? source = null, int limit = 5000) => throw new NotSupportedException();
        }

        private static ApplicationDbContext NewContext() =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        private static async Task<(IngredientEntity Salt, IngredientEntity ButterSalted, IngredientEntity ButterPlain)> SeedFoodsAsync(ApplicationDbContext db)
        {
            var salt = new IngredientEntity { Name = "Salt, table", FdcId = "173468", FdcDataType = "sr_legacy_food", CurationStatusId = 9001 };
            var salted = new IngredientEntity { Name = "Butter, salted", FdcId = "173430", FdcDataType = "sr_legacy_food", CurationStatusId = 9001 };
            var plain = new IngredientEntity { Name = "Butter, without salt", FdcId = "173410", FdcDataType = "sr_legacy_food", CurationStatusId = 9001 };
            db.Ingredients.AddRange(salt, salted, plain);
            await db.SaveChangesAsync();
            return (salt, salted, plain);
        }

        private static async Task<IngredientEntity> UsedIngredientAsync(ApplicationDbContext db, string name, int recipes)
        {
            var ing = new IngredientEntity { Name = name, CurationStatusId = 9000 };
            db.Ingredients.Add(ing);
            await db.SaveChangesAsync();
            for (var i = 0; i < recipes; i++)
            {
                var r = new RecipeEntity { Name = $"{name} recipe {i}" };
                db.Recipes.Add(r);
                await db.SaveChangesAsync();
                db.RecipeIngredients.Add(new RecipeIngredientEntity { RecipeId = r.Id, IngredientId = ing.Id, Quantity = 1, MeasurementId = 1, RawLine = name });
            }
            await db.SaveChangesAsync();
            return ing;
        }

        private static IngredientLinkService Linker(ApplicationDbContext db, FakeMatcher matcher) =>
            new(db, matcher, new MemoryCache(new MemoryCacheOptions()), NullLogger<IngredientLinkService>.Instance);

        [Fact]
        public async Task Most_used_first_exact_names_skip_the_model_and_none_answers_are_not_asked_again()
        {
            using var db = NewContext();
            var (salt, salted, plain) = await SeedFoodsAsync(db);
            var saltIng = await UsedIngredientAsync(db, "salt", 3);
            var butter = await UsedIngredientAsync(db, "butter", 2);
            var weird = await UsedIngredientAsync(db, "xanthan wizardry", 1);
            var matcher = new FakeMatcher
            {
                Answer = q => q.Name == "butter"
                    ? new LinkAnswer(q.IngredientId, q.Candidates.Single(c => c.IngredientId == plain.Id), 0.8m)
                    : new LinkAnswer(q.IngredientId, null, 0.2m),
            };
            var linker = Linker(db, matcher);

            var sources = await linker.NextSourcesAsync(10);
            sources.Select(s => s.Name).Should().Equal("salt", "butter", "xanthan wizardry");

            var result = await linker.ProposeAsync(sources);

            result!.ExactProposals.Should().Be(1);
            result.AiProposals.Should().Be(1);
            result.NoMatch.Should().Be(1);
            matcher.Asked.Should().Equal("butter");
            var proposals = await db.FoodCatalogProposals.ToListAsync();
            proposals.Single(p => p.IngredientId == saltIng.Id).Should().Match<FoodCatalogProposalEntity>(p =>
                p.Status == FoodProposalStatus.Pending && p.Source == IngredientLinkService.ExactSource && p.ProposedValue == salt.Id.ToString());
            proposals.Single(p => p.IngredientId == butter.Id).Should().Match<FoodCatalogProposalEntity>(p =>
                p.Status == FoodProposalStatus.Pending && p.Source == "ai:fake:1b" && p.FdcId == "173410");
            proposals.Single(p => p.IngredientId == weird.Id).Status.Should().Be(FoodProposalStatus.Rejected);

            (await linker.NextSourcesAsync(10)).Should().BeEmpty("every ingredient now has a link decision");
        }

        [Fact]
        public async Task An_ingredient_awaiting_a_same_name_attach_is_not_linked_elsewhere()
        {
            using var db = NewContext();
            await SeedFoodsAsync(db);
            var honey = await UsedIngredientAsync(db, "Honey", 4);
            db.FoodCatalogProposals.Add(new FoodCatalogProposalEntity
            {
                Action = FoodProposalAction.Update, IngredientId = honey.Id, Field = FoodCatalogReviewService.FdcAttachField,
                FdcId = "169640", Source = "fdc:169640", Status = FoodProposalStatus.Pending,
            });
            await db.SaveChangesAsync();

            (await Linker(db, new FakeMatcher()).NextSourcesAsync(10)).Should().BeEmpty();
        }

        [Fact]
        public async Task Approving_a_link_merges_curates_the_food_and_aliases_the_old_name()
        {
            using var db = NewContext();
            var (salt, _, _) = await SeedFoodsAsync(db);
            var saltIng = await UsedIngredientAsync(db, "sea salt", 1);
            var proposal = new FoodCatalogProposalEntity
            {
                Action = FoodProposalAction.Update, IngredientId = saltIng.Id, Field = FoodCatalogReviewService.FdcLinkField,
                FdcId = salt.FdcId, ProposedValue = salt.Id.ToString(), Source = "ai:fake", Status = FoodProposalStatus.Pending,
            };
            db.FoodCatalogProposals.Add(proposal);
            await db.SaveChangesAsync();
            var cleanup = new FakeCleanup();
            var review = new FoodCatalogReviewService(db, new NoAudit(), cleanup);

            (await review.ApplyProposalAsync(proposal.Id, reviewerPersonId: 1)).Should().BeTrue();

            cleanup.Merged.Should().Equal((saltIng.Id, salt.Id));
            var usdaSalt = await db.Ingredients.FindAsync(salt.Id);
            usdaSalt!.CurationStatusId.Should().Be((long)CurationStatusEnum.Curated);
            usdaSalt.Name.Should().Be("Sea Salt", "the USDA food takes the everyday name recipes show");
            (await db.IngredientAliases.Select(a => a.AliasName).ToListAsync()).Should().BeEquivalentTo(new[] { "Salt, table" });
            (await db.FoodCatalogProposals.FindAsync(proposal.Id))!.Status.Should().Be(FoodProposalStatus.Applied);
        }

        [Theory]
        [InlineData("Olive Oil", "Olive Oil")]
        [InlineData("baby spinach", "Baby Spinach")]
        [InlineData("diced red onion", null)]
        [InlineData("salt (or to taste", null)]
        [InlineData("2 cups flour", null)]
        public void Only_clean_names_are_adopted(string name, string? expected)
        {
            FoodCatalogReviewService.FriendlyName(name).Should().Be(expected);
        }

        [Fact]
        public async Task A_link_that_cannot_merge_copies_usda_values_without_overwriting()
        {
            using var db = NewContext();
            var (_, salted, _) = await SeedFoodsAsync(db);
            db.IngredientNutrients.AddRange(
                new IngredientNutrientEntity { IngredientId = salted.Id, NutrientId = 5035, Amount = 717m, MeasurementId = 16 },
                new IngredientNutrientEntity { IngredientId = salted.Id, NutrientId = 5002, Amount = 81m, MeasurementId = 1 });
            var seedButter = new IngredientEntity { Name = "Butter", CurationStatusId = 9003 };
            db.Ingredients.Add(seedButter);
            await db.SaveChangesAsync();
            db.IngredientNutrients.Add(new IngredientNutrientEntity { IngredientId = seedButter.Id, NutrientId = 5002, Amount = 80m, MeasurementId = 1 });
            var proposal = new FoodCatalogProposalEntity
            {
                Action = FoodProposalAction.Update, IngredientId = seedButter.Id, Field = FoodCatalogReviewService.FdcLinkField,
                FdcId = salted.FdcId, ProposedValue = salted.Id.ToString(), Source = IngredientLinkService.ExactSource, Status = FoodProposalStatus.Pending,
            };
            db.FoodCatalogProposals.Add(proposal);
            await db.SaveChangesAsync();
            var review = new FoodCatalogReviewService(db, new NoAudit(), new FakeCleanup { Merges = false });

            (await review.ApplyProposalAsync(proposal.Id, reviewerPersonId: 1)).Should().BeTrue();

            var rows = await db.IngredientNutrients.Where(n => n.IngredientId == seedButter.Id).ToListAsync();
            rows.Single(n => n.NutrientId == 5035).Amount.Should().Be(717m, "the missing calories come from the USDA record");
            rows.Single(n => n.NutrientId == 5002).Amount.Should().Be(80m, "an existing value is never overwritten");
            (await db.Ingredients.FindAsync(seedButter.Id))!.IsDeleted.Should().BeFalse();
        }

        [Fact]
        public async Task A_link_whose_target_changed_is_refused()
        {
            using var db = NewContext();
            var (salt, salted, _) = await SeedFoodsAsync(db);
            var ing = await UsedIngredientAsync(db, "salt", 1);
            var proposal = new FoodCatalogProposalEntity
            {
                Action = FoodProposalAction.Update, IngredientId = ing.Id, Field = FoodCatalogReviewService.FdcLinkField,
                FdcId = salted.FdcId, ProposedValue = salt.Id.ToString(), Source = "ai:fake", Status = FoodProposalStatus.Pending,
            };
            db.FoodCatalogProposals.Add(proposal);
            await db.SaveChangesAsync();

            (await new FoodCatalogReviewService(db, new NoAudit(), new FakeCleanup()).ApplyProposalAsync(proposal.Id, 1))
                .Should().BeFalse("the proposal's FdcId must match the target it names");
        }
    }
}

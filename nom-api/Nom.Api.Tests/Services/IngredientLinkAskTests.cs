using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Nom.Data;
using Nom.Data.Curation;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// The asking side of the link lane: answers are read only by their number, an unanswered
    /// ingredient is not a rejection, a rejection is asked again only when its question changed,
    /// the second opinion is a same-food judge on its own model, and work is ranked by the recipes
    /// a decision would release.
    /// </summary>
    public partial class IngredientLinkTests
    {
        private static readonly IReadOnlyList<FoodCandidate> TwoButters = new[]
        {
            new FoodCandidate(1, "173410", "Butter, without salt", "sr_legacy_food"),
            new FoodCandidate(2, "173430", "Butter, salted", "sr_legacy_food"),
        };

        [Fact]
        public void A_link_answer_is_read_by_its_number_and_never_by_its_position()
        {
            var questions = new[] { new LinkQuestion(10, "creamery butter", TwoButters), new LinkQuestion(11, "sweet butter", TwoButters) };

            OllamaIngredientLinkMatcher.Read("""{"answers":[{"pick":1,"confidence":0.9},{"pick":2,"confidence":0.9}]}""", questions)
                .Should().BeEmpty("entries without a number cannot be told apart");
            OllamaIngredientLinkMatcher.Read("""{"answers":[{"n":2,"pick":0,"confidence":0.4},{"n":1,"pick":2,"confidence":0.8}]}""", questions)
                .Should().Equal(new LinkAnswer(10, TwoButters[1], 0.8m), new LinkAnswer(11, null, 0.4m));
            OllamaIngredientLinkMatcher.Read("""{"answers":[{"n":1,"pick":1},{"n":1,"pick":2},{"n":2,"pick":7}]}""", questions)
                .Should().BeEmpty("a repeated number and a pick that is not an option are both unreadable");
            FluentActions.Invoking(() => OllamaIngredientLinkMatcher.Read("{}", questions)).Should().Throw<InvalidOperationException>();
        }

        [Fact]
        public void A_judgement_is_read_by_its_number_and_must_be_one_of_the_five_words()
        {
            var pairs = new[] { new LinkPair(10, "vanilla", "Vanilla extract"), new LinkPair(11, "cream", "Cream, fluid, heavy whipping"), new LinkPair(12, "salt", "Salt, table") };

            OllamaIngredientLinkVerifier.Read("""{"results":[{"n":3,"verdict":"Same "},{"n":1,"verdict":"variety"},{"n":2,"verdict":"probably"}]}""", pairs)
                .Should().Equal(new LinkVerdict(10, LinkJudgement.VarietyOrForm), new LinkVerdict(12, LinkJudgement.Same));
        }

        [Fact]
        public void The_judge_has_no_default_model()
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ai:OllamaUrl"] = "http://model-host:11434",
                ["Ai:LinkModel"] = "qwen2.5:14b-instruct",
            }).Build();

            new OllamaIngredientLinkVerifier(new HttpClient(), config).IsConfigured.Should().BeFalse();
        }

        [Fact]
        public async Task A_model_rejection_carries_its_question_and_is_not_asked_again_while_the_question_is_unchanged()
        {
            using var db = NewContext();
            await SeedFoodsAsync(db);
            await UsedIngredientAsync(db, "creamery butter", 2);
            var matcher = new FakeMatcher();
            var linker = Linker(db, matcher);

            (await linker.ProposeAsync(await linker.NextSourcesAsync(10)))!.NoMatch.Should().Be(1);
            var rejection = await db.FoodCatalogProposals.SingleAsync();
            rejection.Status.Should().Be(FoodProposalStatus.Rejected);
            rejection.Reason.Should().Contain(LinkAskKey.Tag("fake:1b", 1));
            (await linker.NextSourcesAsync(10)).Should().BeEmpty();

            var old = DateTime.UtcNow - IngredientLinkService.RejectionRetryAge - TimeSpan.FromDays(1);
            rejection.CreatedDate = old;
            rejection.LastModifiedDate = old;
            await db.SaveChangesAsync();
            var result = await linker.ProposeAsync(await linker.NextSourcesAsync(10));

            result!.Unchanged.Should().Be(1);
            matcher.Asked.Should().Equal(new[] { "creamery butter" }, "the same model, prompt and shortlist give the same answer");
            (await db.FoodCatalogProposals.CountAsync()).Should().Be(1);
            rejection.LastModifiedDate.Should().BeAfter(old);
            (await linker.NextSourcesAsync(10)).Should().BeEmpty();
        }

        [Fact]
        public async Task A_new_prompt_version_asks_a_rejection_again_at_once_and_updates_its_row()
        {
            using var db = NewContext();
            await SeedFoodsAsync(db);
            await UsedIngredientAsync(db, "creamery butter", 2);
            var matcher = new FakeMatcher();
            await Linker(db, matcher).ProposeAsync(await Linker(db, matcher).NextSourcesAsync(10));

            matcher.PromptVersion = 2;
            var linker = Linker(db, matcher);
            var sources = await linker.NextSourcesAsync(10);
            sources.Select(s => s.Name).Should().Equal("creamery butter");
            await linker.ProposeAsync(sources);

            matcher.Asked.Should().HaveCount(2);
            var row = await db.FoodCatalogProposals.SingleAsync();
            row.Status.Should().Be(FoodProposalStatus.Rejected);
            row.Reason.Should().Contain(LinkAskKey.Tag("fake:1b", 2)).And.NotContain(LinkAskKey.Tag("fake:1b", 1));
        }

        [Fact]
        public async Task An_ingredient_the_model_leaves_unanswered_gets_no_row_and_after_two_tries_is_held_for_the_day()
        {
            using var db = NewContext();
            await SeedFoodsAsync(db);
            await UsedIngredientAsync(db, "creamery butter", 2);
            var matcher = new FakeMatcher { Answer = _ => null };
            var linker = Linker(db, matcher);

            (await linker.ProposeAsync(await linker.NextSourcesAsync(10)))!.Unreadable.Should().Be(1);
            (await db.FoodCatalogProposals.CountAsync()).Should().Be(0, "a missing answer is not a rejection");
            (await linker.NextSourcesAsync(10)).Should().HaveCount(1);

            await linker.ProposeAsync(await linker.NextSourcesAsync(10));

            (await db.FoodCatalogProposals.CountAsync()).Should().Be(0);
            (await linker.NextSourcesAsync(10)).Should().BeEmpty("it does not take a place in every pass");
        }

        [Fact]
        public async Task An_unreachable_model_while_asking_one_at_a_time_ends_the_pass_without_rejections()
        {
            using var db = NewContext();
            await SeedFoodsAsync(db);
            await UsedIngredientAsync(db, "creamery butter", 2);
            var calls = 0;
            var matcher = new FakeMatcher
            {
                Failure = _ => ++calls == 1 ? new JsonException("garbled") : new HttpRequestException("connection refused"),
            };
            var linker = Linker(db, matcher);

            (await linker.ProposeAsync(await linker.NextSourcesAsync(10))).Should().BeNull();

            (await db.FoodCatalogProposals.CountAsync()).Should().Be(0);
        }

        [Fact]
        public async Task The_judge_never_judges_its_own_models_picks_and_without_a_model_checks_nothing()
        {
            using var db = NewContext();
            var (_, _, plain) = await SeedFoodsAsync(db);
            var butter = await UsedIngredientAsync(db, "unsalted butter", 2);
            var proposal = await AiPickAsync(db, butter, plain, 0.95m);

            var same = new FakeVerifier { ModelName = "fake:1b" };
            (await Linker(db, new FakeMatcher(), same).VerifyPendingAiAsync(10, 0.85m)).Should().Be(new IngredientLinkVerifyResult(0, 0, false));
            var none = new FakeVerifier { IsConfigured = false };
            (await Linker(db, new FakeMatcher(), none).VerifyPendingAiAsync(10, 0.85m)).Should().Be(new IngredientLinkVerifyResult(0, 0, false));

            same.Asked.Should().BeEmpty();
            none.Asked.Should().BeEmpty();
            proposal.Reason.Should().NotContain(IngredientLinkService.TriangulationMark);
        }

        private static async Task<RecipeEntity> ApprovableRecipeAsync(ApplicationDbContext db, string name, params (IngredientEntity Ingredient, decimal Quantity)[] lines)
        {
            var recipe = new RecipeEntity
            {
                Name = name, CurationStatusId = (long)CurationStatusEnum.NonCurated, Visibility = RecipeVisibilityEnum.Public,
                LicenseStatus = RecipeLicenseStatus.PublicDomain,
            };
            db.Recipes.Add(recipe);
            await db.SaveChangesAsync();
            foreach (var (ingredient, quantity) in lines)
            {
                db.RecipeIngredients.Add(new RecipeIngredientEntity { RecipeId = recipe.Id, IngredientId = ingredient.Id, Quantity = quantity, MeasurementId = 1, RawLine = ingredient.Name });
            }
            await db.SaveChangesAsync();
            return recipe;
        }

        [Fact]
        public async Task Wordings_are_ranked_by_the_recipes_they_alone_keep_from_approval()
        {
            using var db = NewContext();
            var (salt, _, _) = await SeedFoodsAsync(db);
            var curatedSalt = new IngredientEntity { Name = "Salt", CurationStatusId = (long)CurationStatusEnum.Curated };
            var vanilla = new IngredientEntity { Name = "vanilla", CurationStatusId = (long)CurationStatusEnum.NonCurated };
            var rind = new IngredientEntity { Name = "lemon rind", CurationStatusId = (long)CurationStatusEnum.NonCurated };
            db.Ingredients.AddRange(curatedSalt, vanilla, rind);
            await db.SaveChangesAsync();
            await ApprovableRecipeAsync(db, "cake", (vanilla, 1), (curatedSalt, 1));
            await ApprovableRecipeAsync(db, "custard", (vanilla, 2), (rind, 0));
            await ApprovableRecipeAsync(db, "tart", (rind, 1));
            await ApprovableRecipeAsync(db, "pudding", (rind, 1), (vanilla, 1));
            var unlicensed = await ApprovableRecipeAsync(db, "scraped", (rind, 1));
            unlicensed.LicenseStatus = RecipeLicenseStatus.Unknown;
            unlicensed.ScrapedAtUtc = DateTime.UtcNow;
            db.FoodCatalogProposals.Add(new FoodCatalogProposalEntity
            {
                Action = FoodProposalAction.Update, Batch = IngredientLinkService.Batch, IngredientId = vanilla.Id, Field = FoodCatalogReviewService.FdcLinkField,
                CurrentValue = "vanilla", ProposedValue = salt.Id.ToString(), FdcId = salt.FdcId, Source = "ai:fake:1b", Confidence = 0.9m,
                Reason = "→ Salt, table · used in 3 recipes" + IngredientLinkService.NotVerifiedMark + " — fake:verify prompt 1 judges it a different food",
                Status = FoodProposalStatus.Pending,
            });
            await db.SaveChangesAsync();

            var rows = await Linker(db, new FakeMatcher()).TopBlockersAsync(10);

            rows.Select(r => (r.Wording, r.RecipesUnlocked, r.Lines)).Should().Equal(("vanilla", 2, 2), ("lemon rind", 1, 1));
            rows[0].Proposal!.Should().Match<LinkImpactProposal>(p => p.TargetName == "Salt, table" && p.Status == FoodProposalStatus.Pending
                && p.Verification == "not verified — fake:verify prompt 1 judges it a different food");
            rows[1].Proposal.Should().BeNull();
        }

        [Fact]
        public async Task The_second_opinion_checks_first_the_picks_that_release_the_most_recipes()
        {
            using var db = NewContext();
            var (salt, _, plain) = await SeedFoodsAsync(db);
            var butter = await UsedIngredientAsync(db, "unsalted butter", 5);
            var vanilla = new IngredientEntity { Name = "vanilla", CurationStatusId = (long)CurationStatusEnum.NonCurated };
            db.Ingredients.Add(vanilla);
            await db.SaveChangesAsync();
            await ApprovableRecipeAsync(db, "cake", (vanilla, 1));
            var butterPick = await AiPickAsync(db, butter, plain, 0.95m);
            var vanillaPick = await AiPickAsync(db, vanilla, salt, 0.95m);

            (await Linker(db, new FakeMatcher(), new FakeVerifier()).VerifyPendingAiAsync(1, 0.85m))!.Checked.Should().Be(1);

            vanillaPick.Reason.Should().Contain(IngredientLinkService.TriangulationMark);
            butterPick.Reason.Should().NotContain(IngredientLinkService.TriangulationMark, "it is used more but releases no recipe on its own");
        }
    }
}

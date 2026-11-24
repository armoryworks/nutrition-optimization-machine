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
using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// Catalog ingredients get linked to USDA foods through admin-reviewed proposals: exact name
    /// matches are proposed deterministically, ambiguous ones by the model choosing among real USDA
    /// rows, and approving a link merges or copies USDA data — never a model-authored number.
    /// </summary>
    public partial class IngredientLinkTests
    {
        private sealed class FakeMatcher : IIngredientLinkMatcher
        {
            public Func<LinkQuestion, LinkAnswer?> Answer { get; set; } = q => new LinkAnswer(q.IngredientId, null, 0m);
            public Func<IReadOnlyList<LinkQuestion>, Exception?> Failure { get; set; } = _ => null;
            public List<string> Asked { get; } = new();
            public bool IsConfigured => true;
            public string ModelName => "fake:1b";
            public int PromptVersion { get; set; } = 1;

            public Task<IReadOnlyList<LinkAnswer>> ChooseAsync(IReadOnlyList<LinkQuestion> questions, CancellationToken cancellationToken = default)
            {
                Asked.AddRange(questions.Select(q => q.Name));
                if (Failure(questions) is { } failure) throw failure;
                return Task.FromResult<IReadOnlyList<LinkAnswer>>(questions.Select(Answer).OfType<LinkAnswer>().ToList());
            }
        }

        private sealed class FakeCleanup : ICatalogCleanupService
        {
            public bool Merges { get; set; } = true;
            public List<(long Source, long Target)> Merged { get; } = new();
            public List<long?> KeptProposals { get; } = new();
            public Task<CatalogCleanupPreview> PreviewAsync(int sampleSize = 50) => throw new NotSupportedException();
            public Task<CatalogCleanupResult> ApplyAsync(int maxActions = 500) => throw new NotSupportedException();

            public Task<bool> MergeIntoAsync(long sourceId, long targetId, long? personId, bool ignoreProposals = false, long? keepProposalId = null)
            {
                if (Merges) Merged.Add((sourceId, targetId));
                KeptProposals.Add(keepProposalId);
                return Task.FromResult(Merges);
            }

            public Task<CatalogNormalizeResult> NormalizeAsync(bool apply) => throw new NotSupportedException();
            public Task<bool> LowersDietAsync(long sourceId, long targetId) => Task.FromResult(false);
            public Task<(int Lines, int Held, IReadOnlyList<LineMoveHold> Holds)> DietHoldsAsync(long sourceId, long targetId) => Task.FromResult((0, 0, (IReadOnlyList<LineMoveHold>)Array.Empty<LineMoveHold>()));
            public Task<IReadOnlyList<string>> RaisesDietAsync(long targetId, string? name, IReadOnlyList<(string Alias, string? Context)> aliases) => Task.FromResult((IReadOnlyList<string>)Array.Empty<string>());
        }

        private sealed class NoAudit : IFoodCatalogAuditService
        {
            public Task<FoodCatalogAuditResult> AuditAsync(string? source = null, int limit = 5000, long afterId = 0) => throw new NotSupportedException();
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

        private sealed class FakeVerifier : IIngredientLinkVerifier
        {
            public Func<LinkPair, LinkJudgement?> Judge { get; set; } = _ => LinkJudgement.Same;
            public Exception? Failure { get; set; }
            public List<LinkPair> Asked { get; } = new();
            public bool IsConfigured { get; set; } = true;
            public string ModelName { get; set; } = "fake:verify";
            public int PromptVersion => 1;

            public Task<IReadOnlyList<LinkVerdict>> JudgeAsync(IReadOnlyList<LinkPair> pairs, CancellationToken cancellationToken = default)
            {
                Asked.AddRange(pairs);
                if (Failure != null) throw Failure;
                return Task.FromResult<IReadOnlyList<LinkVerdict>>(pairs
                    .Select(p => Judge(p) is { } judgement ? new LinkVerdict(p.IngredientId, judgement) : null)
                    .OfType<LinkVerdict>().ToList());
            }
        }

        private static IngredientLinkService Linker(ApplicationDbContext db, FakeMatcher matcher, FakeVerifier? verifier = null) =>
            new(db, matcher, verifier ?? new FakeVerifier(), new MemoryCache(new MemoryCacheOptions()), NullLogger<IngredientLinkService>.Instance);

        private static async Task<FoodCatalogProposalEntity> AiPickAsync(ApplicationDbContext db, IngredientEntity ing, IngredientEntity target, decimal confidence)
        {
            var proposal = new FoodCatalogProposalEntity
            {
                Action = FoodProposalAction.Update, Batch = IngredientLinkService.Batch, IngredientId = ing.Id, Field = FoodCatalogReviewService.FdcLinkField,
                CurrentValue = ing.Name, ProposedValue = target.Id.ToString(), FdcId = target.FdcId, Source = "ai:fake:1b", Confidence = confidence,
                Reason = $"→ {target.Name} · used in 2 recipes", Status = FoodProposalStatus.Pending,
            };
            db.FoodCatalogProposals.Add(proposal);
            await db.SaveChangesAsync();
            return proposal;
        }

        [Fact]
        public async Task A_model_pick_all_three_checks_agree_on_is_verified_and_applies_as_the_system()
        {
            using var db = NewContext();
            var (_, _, plain) = await SeedFoodsAsync(db);
            var butter = await UsedIngredientAsync(db, "unsalted butter", 2);
            var proposal = await AiPickAsync(db, butter, plain, 0.92m);
            var verifier = new FakeVerifier();
            var linker = Linker(db, new FakeMatcher(), verifier);

            var result = await linker.VerifyPendingAiAsync(10, 0.85m);

            result.Should().Be(new IngredientLinkVerifyResult(1, 1, false));
            verifier.Asked.Single().Should().Be(new LinkPair(butter.Id, "unsalted butter", "Butter, without salt"), "the judge sees the wording and the one description picked");
            proposal.Reason.Should().Contain(IngredientLinkService.VerifiedMark).And.Contain("fake:verify");
            proposal.Source.Should().Be("ai:fake:1b", "verification is recorded beside the provenance, not instead of it");
            var ids = await linker.PendingVerifiedAsync(10, Array.Empty<long>());
            ids.Should().Equal(proposal.Id);

            var review = new FoodCatalogReviewService(db, new NoAudit(), new FakeCleanup());
            (await review.ApplyProposalAsync(ids[0], Nom.Data.SystemConstants.SystemPersonId)).Should().BeTrue();
            (await db.FoodCatalogProposals.FindAsync(proposal.Id))!.Should().Match<FoodCatalogProposalEntity>(p =>
                p.Status == FoodProposalStatus.Applied && p.ReviewedByPersonId == Nom.Data.SystemConstants.SystemPersonId);
        }

        [Fact]
        public async Task A_pick_below_the_confidence_bar_is_neither_asked_nor_marked()
        {
            using var db = NewContext();
            var (_, _, plain) = await SeedFoodsAsync(db);
            var butter = await UsedIngredientAsync(db, "unsalted butter", 2);
            var proposal = await AiPickAsync(db, butter, plain, 0.8m);
            var verifier = new FakeVerifier();
            var linker = Linker(db, new FakeMatcher(), verifier);

            (await linker.VerifyPendingAiAsync(10, 0.85m)).Should().Be(new IngredientLinkVerifyResult(0, 0, false));

            verifier.Asked.Should().BeEmpty();
            proposal.Reason.Should().NotContain(IngredientLinkService.TriangulationMark, "a lower bar later should still reach it");
            proposal.Status.Should().Be(FoodProposalStatus.Pending);
            (await linker.PendingVerifiedAsync(10, Array.Empty<long>())).Should().BeEmpty();
        }

        [Fact]
        public async Task A_pick_the_name_matcher_does_not_shortlist_near_the_top_stays_pending_without_asking_the_model()
        {
            using var db = NewContext();
            await SeedFoodsAsync(db);
            var honey = new IngredientEntity { Name = "Honey", FdcId = "169640", FdcDataType = "sr_legacy_food", CurationStatusId = 9001 };
            db.Ingredients.Add(honey);
            await db.SaveChangesAsync();
            var butter = await UsedIngredientAsync(db, "unsalted butter", 2);
            var proposal = await AiPickAsync(db, butter, honey, 0.97m);
            var verifier = new FakeVerifier();
            var linker = Linker(db, new FakeMatcher(), verifier);

            (await linker.VerifyPendingAiAsync(10, 0.85m)).Should().Be(new IngredientLinkVerifyResult(1, 0, false));

            verifier.Asked.Should().BeEmpty();
            proposal.Reason.Should().Contain("not verified — name matcher ranks Butter, without salt first");
            proposal.Status.Should().Be(FoodProposalStatus.Pending);
            (await linker.PendingVerifiedAsync(10, Array.Empty<long>())).Should().BeEmpty();
        }

        [Fact]
        public async Task A_pick_the_name_matcher_ranks_second_is_not_verified_and_not_sent_to_the_judge()
        {
            using var db = NewContext();
            var (_, salted, _) = await SeedFoodsAsync(db);
            var butter = await UsedIngredientAsync(db, "unsalted butter", 2);
            var proposal = await AiPickAsync(db, butter, salted, 0.97m);
            var verifier = new FakeVerifier();
            var linker = Linker(db, new FakeMatcher(), verifier);

            (await linker.VerifyPendingAiAsync(10, 0.85m)).Should().Be(new IngredientLinkVerifyResult(1, 0, false));

            verifier.Asked.Should().BeEmpty("only the matcher's first choice is judged, the rule that measured best");
            proposal.Reason.Should().Contain("not verified — name matcher ranks Butter, without salt first");
        }

        [Fact]
        public async Task A_verified_pick_below_the_apply_bar_waits_for_an_admin()
        {
            using var db = NewContext();
            var (_, _, plain) = await SeedFoodsAsync(db);
            var butter = await UsedIngredientAsync(db, "unsalted butter", 2);
            var proposal = await AiPickAsync(db, butter, plain, 0.82m);
            var linker = Linker(db, new FakeMatcher(), new FakeVerifier());

            (await linker.VerifyPendingAiAsync(10, 0.80m)).Should().Be(new IngredientLinkVerifyResult(1, 1, false));

            proposal.Reason.Should().Contain(IngredientLinkService.VerifiedMark);
            (await linker.PendingVerifiedAsync(10, Array.Empty<long>(), 0.85m)).Should().BeEmpty();
            (await linker.PendingVerifiedAsync(10, Array.Empty<long>(), 0.80m)).Should().Equal(proposal.Id);
        }

        [Fact]
        public async Task A_quantity_prefixed_name_is_matched_and_asked_about_by_its_food_name()
        {
            using var db = NewContext();
            var (salt, _, plain) = await SeedFoodsAsync(db);
            var prefixedSalt = await UsedIngredientAsync(db, "1 teaspoon salt", 3);
            var prefixedButter = await UsedIngredientAsync(db, "2 tablespoons of creamery butter", 2);
            var matcher = new FakeMatcher
            {
                Answer = q => new LinkAnswer(q.IngredientId, q.Candidates.Single(c => c.IngredientId == plain.Id), 0.9m),
            };
            var linker = Linker(db, matcher);

            var result = await linker.ProposeAsync(await linker.NextSourcesAsync(10));

            result!.ExactProposals.Should().Be(1);
            result.AiProposals.Should().Be(1);
            matcher.Asked.Should().Equal("creamery butter");
            var proposals = await db.FoodCatalogProposals.ToListAsync();
            proposals.Single(p => p.IngredientId == prefixedSalt.Id).Should().Match<FoodCatalogProposalEntity>(p =>
                p.ProposedValue == salt.Id.ToString() && p.CurrentValue == "1 teaspoon salt");
            proposals.Single(p => p.IngredientId == prefixedButter.Id).CurrentValue.Should().Be("2 tablespoons of creamery butter", "the catalog name is kept as written");
        }

        [Fact]
        public async Task A_food_the_catalog_rejected_is_never_a_link_candidate()
        {
            using var db = NewContext();
            var (salt, _, plain) = await SeedFoodsAsync(db);
            salt.CurationStatusId = (long)CurationStatusEnum.Rejected;
            plain.CurationStatusId = (long)CurationStatusEnum.Rejected;
            await db.SaveChangesAsync();
            var prefixedSalt = await UsedIngredientAsync(db, "1 teaspoon salt", 3);
            await UsedIngredientAsync(db, "2 tablespoons of creamery butter", 2);
            var matcher = new FakeMatcher();
            var offered = new List<long>();
            matcher.Answer = q =>
            {
                offered.AddRange(q.Candidates.Select(c => c.IngredientId));
                return new LinkAnswer(q.IngredientId, null, 0m);
            };
            var linker = Linker(db, matcher);

            await linker.ProposeAsync(await linker.NextSourcesAsync(10));

            (await db.FoodCatalogProposals.AnyAsync(p => p.IngredientId == prefixedSalt.Id && p.ProposedValue == salt.Id.ToString()))
                .Should().BeFalse("the staple rule never names a rejected food");
            offered.Should().NotContain(new[] { salt.Id, plain.Id }, "the model picker is never shown a rejected food");
        }

        [Fact]
        public async Task A_prepared_food_is_never_a_link_candidate_even_when_curated()
        {
            using var db = NewContext();
            var (salt, _, plain) = await SeedFoodsAsync(db);
            salt.CurationStatusId = (long)CurationStatusEnum.Curated;
            salt.FdcCategoryId = 21;
            plain.CurationStatusId = (long)CurationStatusEnum.Curated;
            plain.FdcCategoryId = 3;
            await db.SaveChangesAsync();
            var prefixedSalt = await UsedIngredientAsync(db, "1 teaspoon salt", 3);
            await UsedIngredientAsync(db, "2 tablespoons of creamery butter", 2);
            var matcher = new FakeMatcher();
            var offered = new List<long>();
            matcher.Answer = q =>
            {
                offered.AddRange(q.Candidates.Select(c => c.IngredientId));
                return new LinkAnswer(q.IngredientId, null, 0m);
            };
            var linker = Linker(db, matcher);

            await linker.ProposeAsync(await linker.NextSourcesAsync(10));

            (await db.FoodCatalogProposals.AnyAsync(p => p.IngredientId == prefixedSalt.Id && p.ProposedValue == salt.Id.ToString()))
                .Should().BeFalse("the staple and name rules never name a prepared food");
            offered.Should().NotContain(new[] { salt.Id, plain.Id }, "the model picker is never shown a prepared food");
        }

        [Fact]
        public async Task The_bulk_status_endpoint_journals_every_change_with_who_made_it()
        {
            using var db = NewContext();
            var (salt, salted, _) = await SeedFoodsAsync(db);
            salt.CurationStatusId = (long)CurationStatusEnum.Curated;
            await db.SaveChangesAsync();

            await new FoodCatalogReviewService(db, new NoAudit(), new FakeCleanup())
                .SetCurationStatusAsync(new[] { salt.Id, salted.Id }, (long)CurationStatusEnum.Curated, personId: 42);

            var entry = await db.AuditLogEntries.SingleAsync();
            entry.Should().Match<Nom.Data.Audit.AuditLogEntryEntity>(a => a.EntityId == salted.Id && a.EntityType == "Ingredient"
                && a.ChangeType == FoodCatalogReviewService.CurationStatusSetChangeType && a.PropertyName == "CurationStatusId"
                && a.OldValue == "9001" && a.NewValue == "9003" && a.ChangedByPersonId == 42);
        }

        [Fact]
        public async Task A_machine_rejection_is_retried_after_a_week_but_a_persons_is_final()
        {
            using var db = NewContext();
            await SeedFoodsAsync(db);
            var fresh = await UsedIngredientAsync(db, "xanthan wizardry", 1);
            var stale = await UsedIngredientAsync(db, "dragon fruit", 1);
            var declined = await UsedIngredientAsync(db, "unicorn salt", 1);
            var old = DateTime.UtcNow - IngredientLinkService.RejectionRetryAge - TimeSpan.FromDays(1);
            db.FoodCatalogProposals.AddRange(
                Rejection(fresh.Id, DateTime.UtcNow, null),
                Rejection(stale.Id, old, null),
                Rejection(declined.Id, old, 42));
            await db.SaveChangesAsync();

            var sources = await Linker(db, new FakeMatcher()).NextSourcesAsync(10);

            sources.Select(s => s.Name).Should().Equal("dragon fruit");
        }

        private static FoodCatalogProposalEntity Rejection(long ingredientId, DateTime when, long? reviewer) => new()
        {
            Action = FoodProposalAction.Update, Batch = IngredientLinkService.Batch, IngredientId = ingredientId,
            Field = FoodCatalogReviewService.FdcLinkField, Source = IngredientLinkService.NoCandidateSource,
            Status = FoodProposalStatus.Rejected, CreatedDate = when, ReviewedByPersonId = reviewer, ReviewedAt = reviewer == null ? null : when,
        };

        [Fact]
        public async Task A_renamed_usda_food_is_matched_by_the_description_it_kept_as_an_alias()
        {
            using var db = NewContext();
            var (_, _, plain) = await SeedFoodsAsync(db);
            var oliveOil = new IngredientEntity { Name = "Olive Oil", FdcId = "171413", FdcDataType = "sr_legacy_food", CurationStatusId = 9003 };
            db.Ingredients.Add(oliveOil);
            await db.SaveChangesAsync();
            db.IngredientAliases.AddRange(
                new IngredientAliasEntity { IngredientId = oliveOil.Id, AliasName = "Oil, olive, salad or cooking", SourceContext = IngredientLinkService.UsdaDescriptionContext },
                new IngredientAliasEntity { IngredientId = oliveOil.Id, AliasName = "2 tablespoons olive oil, for frying" },
                new IngredientAliasEntity { IngredientId = oliveOil.Id, AliasName = "Sherry, or other white wine", SourceContext = "link:ai:fake:1b" });
            await db.SaveChangesAsync();
            var ing = await UsedIngredientAsync(db, "extra virgin olive oil", 2);
            var sherry = await UsedIngredientAsync(db, "sherry", 2);
            var matcher = new FakeMatcher();

            await Linker(db, matcher).ProposeAsync(await Linker(db, matcher).NextSourcesAsync(10));

            var proposal = await db.FoodCatalogProposals.SingleAsync(p => p.IngredientId == ing.Id);
            proposal.ProposedValue.Should().Be(oliveOil.Id.ToString());
            proposal.Reason.Should().StartWith("→ Oil, olive, salad or cooking");
            var sherryProposal = await db.FoodCatalogProposals.SingleAsync(p => p.IngredientId == sherry.Id);
            sherryProposal.Status.Should().Be(FoodProposalStatus.Rejected, "a recipe wording that an earlier link left as an alias is not the food's description");
            sherryProposal.ProposedValue.Should().BeNull();
        }

        [Theory]
        [InlineData(LinkJudgement.VarietyOrForm, "not verified — fake:verify prompt 1 judges it a different variety or form")]
        [InlineData(LinkJudgement.DifferentFood, "not verified — fake:verify prompt 1 judges it a different food")]
        [InlineData(LinkJudgement.MoreThanOneFood, "not verified — fake:verify prompt 1 judges it more than one food")]
        [InlineData(LinkJudgement.Unsure, IngredientLinkService.TriangulationMark + "unsure — fake:verify prompt 1 is unsure")]
        public async Task A_pick_the_judge_does_not_call_the_same_food_stays_pending(LinkJudgement judgement, string mark)
        {
            using var db = NewContext();
            var (_, _, plain) = await SeedFoodsAsync(db);
            var butter = await UsedIngredientAsync(db, "unsalted butter", 2);
            var proposal = await AiPickAsync(db, butter, plain, 0.95m);
            var verifier = new FakeVerifier { Judge = _ => judgement };
            var linker = Linker(db, new FakeMatcher(), verifier);

            (await linker.VerifyPendingAiAsync(10, 0.85m)).Should().Be(new IngredientLinkVerifyResult(1, 0, false));

            proposal.Reason.Should().Contain(mark).And.NotContain(IngredientLinkService.VerifiedMark);
            proposal.Status.Should().Be(FoodProposalStatus.Pending);
            (await linker.PendingVerifiedAsync(10, Array.Empty<long>())).Should().BeEmpty();
        }

        [Fact]
        public async Task An_unreadable_second_opinion_is_not_a_verification()
        {
            using var db = NewContext();
            var (_, _, plain) = await SeedFoodsAsync(db);
            var butter = await UsedIngredientAsync(db, "unsalted butter", 2);
            var proposal = await AiPickAsync(db, butter, plain, 0.95m);
            var verifier = new FakeVerifier { Failure = new System.Text.Json.JsonException("garbled") };
            var linker = Linker(db, new FakeMatcher(), verifier);

            (await linker.VerifyPendingAiAsync(10, 0.85m)).Should().Be(new IngredientLinkVerifyResult(1, 0, false));

            proposal.Reason.Should().Contain("not verified — fake:verify prompt 1 gave no readable answer");
            (await linker.PendingVerifiedAsync(10, Array.Empty<long>())).Should().BeEmpty();
        }

        [Fact]
        public async Task A_checked_pick_is_not_asked_again()
        {
            using var db = NewContext();
            var (_, salted, plain) = await SeedFoodsAsync(db);
            var butter = await UsedIngredientAsync(db, "unsalted butter", 2);
            await AiPickAsync(db, butter, plain, 0.95m);
            var verifier = new FakeVerifier { Judge = _ => LinkJudgement.DifferentFood };
            await Linker(db, new FakeMatcher(), verifier).VerifyPendingAiAsync(10, 0.85m);
            verifier.Asked.Should().HaveCount(1);

            var again = new FakeVerifier();
            (await Linker(db, new FakeMatcher(), again).VerifyPendingAiAsync(10, 0.85m)).Should().Be(new IngredientLinkVerifyResult(0, 0, false));

            again.Asked.Should().BeEmpty("the outcome is stored on the proposal, so a restart doesn't ask again");
        }

        [Fact]
        public async Task An_unreachable_second_opinion_verifies_nothing_and_leaves_picks_for_later()
        {
            using var db = NewContext();
            var (_, _, plain) = await SeedFoodsAsync(db);
            var butter = await UsedIngredientAsync(db, "unsalted butter", 2);
            var proposal = await AiPickAsync(db, butter, plain, 0.95m);
            var verifier = new FakeVerifier { Failure = new System.Net.Http.HttpRequestException("connection refused") };
            var linker = Linker(db, new FakeMatcher(), verifier);

            (await linker.VerifyPendingAiAsync(10, 0.85m)).Should().BeNull();

            proposal.Reason.Should().NotContain(IngredientLinkService.TriangulationMark);
            proposal.Status.Should().Be(FoodProposalStatus.Pending);
            (await linker.PendingVerifiedAsync(10, Array.Empty<long>())).Should().BeEmpty();

            verifier.Failure = null;
            (await linker.VerifyPendingAiAsync(10, 0.85m))!.Verified.Should().Be(1, "it is checked once the model is back");
        }

        [Fact]
        public async Task Most_used_first_exact_names_skip_the_model_and_none_answers_are_not_asked_again()
        {
            using var db = NewContext();
            var (salt, salted, plain) = await SeedFoodsAsync(db);
            var saltIng = await UsedIngredientAsync(db, "salt", 3);
            var butter = await UsedIngredientAsync(db, "creamery butter", 2);
            var weird = await UsedIngredientAsync(db, "xanthan wizardry", 1);
            var matcher = new FakeMatcher
            {
                Answer = q => q.Name == "creamery butter"
                    ? new LinkAnswer(q.IngredientId, q.Candidates.Single(c => c.IngredientId == plain.Id), 0.8m)
                    : new LinkAnswer(q.IngredientId, null, 0.2m),
            };
            var linker = Linker(db, matcher);

            var sources = await linker.NextSourcesAsync(10);
            sources.Select(s => s.Name).Should().Equal("salt", "creamery butter", "xanthan wizardry");

            var result = await linker.ProposeAsync(sources);

            result!.ExactProposals.Should().Be(1);
            result.AiProposals.Should().Be(1);
            result.NoMatch.Should().Be(1);
            matcher.Asked.Should().Equal("creamery butter");
            var proposals = await db.FoodCatalogProposals.ToListAsync();
            proposals.Single(p => p.IngredientId == saltIng.Id).Should().Match<FoodCatalogProposalEntity>(p =>
                p.Status == FoodProposalStatus.Pending && p.Source.StartsWith("deterministic:") && p.ProposedValue == salt.Id.ToString());
            proposals.Single(p => p.IngredientId == butter.Id).Should().Match<FoodCatalogProposalEntity>(p =>
                p.Status == FoodProposalStatus.Pending && p.Source == "ai:fake:1b" && p.FdcId == "173410");
            proposals.Single(p => p.IngredientId == weird.Id).Status.Should().Be(FoodProposalStatus.Rejected);

            (await linker.NextSourcesAsync(10)).Should().BeEmpty("every ingredient now has a link decision");
        }

        [Fact]
        public async Task A_connection_error_while_asking_one_at_a_time_records_no_rejection()
        {
            using var db = NewContext();
            await SeedFoodsAsync(db);
            var butter = await UsedIngredientAsync(db, "creamery butter", 2);
            var calls = 0;
            var matcher = new FakeMatcher
            {
                Answer = q => throw (calls++ == 0
                    ? new System.Text.Json.JsonException("garbled")
                    : new System.Net.Http.HttpRequestException("connection refused")),
            };
            var linker = Linker(db, matcher);

            (await linker.ProposeAsync(await linker.NextSourcesAsync(10))).Should().BeNull();

            (await db.FoodCatalogProposals.AnyAsync(p => p.IngredientId == butter.Id)).Should().BeFalse();
            (await linker.NextSourcesAsync(10)).Select(s => s.IngredientId).Should().Contain(butter.Id, "it is asked again once the model is back");
        }

        [Fact]
        public async Task A_bare_staple_name_takes_its_standard_usda_entry_without_the_model()
        {
            using var db = NewContext();
            var (_, salted, _) = await SeedFoodsAsync(db);
            var butter = await UsedIngredientAsync(db, "Butter", 5);
            var flour = await UsedIngredientAsync(db, "flour", 4);
            var matcher = new FakeMatcher();
            var linker = Linker(db, matcher);

            var result = await linker.ProposeAsync(await linker.NextSourcesAsync(10));

            matcher.Asked.Should().NotContain("Butter");
            result!.ExactProposals.Should().Be(1);
            (await db.FoodCatalogProposals.SingleAsync(p => p.IngredientId == butter.Id)).Should().Match<FoodCatalogProposalEntity>(p =>
                p.Status == FoodProposalStatus.Pending && p.Source == IngredientLinkService.StapleSource && p.FdcId == salted.FdcId);
            (await db.FoodCatalogProposals.SingleAsync(p => p.IngredientId == flour.Id)).Status.Should().Be(FoodProposalStatus.Rejected, "a staple whose USDA entry isn't in the catalog falls through to the usual matching");
        }

        [Fact]
        public async Task Pending_model_links_for_staples_are_repointed_but_reviewed_ones_are_left_alone()
        {
            using var db = NewContext();
            var (_, salted, plain) = await SeedFoodsAsync(db);
            var butter = await UsedIngredientAsync(db, "butter", 5);
            var salt = await UsedIngredientAsync(db, "salt", 3);
            db.FoodCatalogProposals.AddRange(
                new FoodCatalogProposalEntity
                {
                    Action = FoodProposalAction.Update, Batch = IngredientLinkService.Batch, IngredientId = butter.Id, Field = FoodCatalogReviewService.FdcLinkField,
                    CurrentValue = "butter", ProposedValue = plain.Id.ToString(), FdcId = plain.FdcId, Source = "ai:fake:1b", Confidence = 0.7m,
                    Reason = "→ Butter, without salt · used in 5 recipes", Status = FoodProposalStatus.Pending,
                },
                new FoodCatalogProposalEntity
                {
                    Action = FoodProposalAction.Update, Batch = IngredientLinkService.Batch, IngredientId = salt.Id, Field = FoodCatalogReviewService.FdcLinkField,
                    CurrentValue = "salt", ProposedValue = plain.Id.ToString(), FdcId = plain.FdcId, Source = "ai:fake:1b", Status = FoodProposalStatus.Rejected,
                });
            await db.SaveChangesAsync();

            (await Linker(db, new FakeMatcher()).ApplyStapleDefaultsToPendingAsync()).Should().Be(1);

            var proposals = await db.FoodCatalogProposals.ToListAsync();
            proposals.Single(p => p.IngredientId == butter.Id).Should().Match<FoodCatalogProposalEntity>(p =>
                p.FdcId == salted.FdcId && p.ProposedValue == salted.Id.ToString() && p.Source == IngredientLinkService.StapleSource
                && p.Reason == "→ Butter, salted · used in 5 recipes");
            proposals.Single(p => p.IngredientId == salt.Id).FdcId.Should().Be(plain.FdcId);
        }

        [Fact]
        public async Task Only_staples_and_same_name_attaches_are_applied_without_an_admin()
        {
            using var db = NewContext();
            var (salt, salted, plain) = await SeedFoodsAsync(db);
            var butter = await UsedIngredientAsync(db, "butter", 5);
            var honey = await UsedIngredientAsync(db, "Honey", 4);
            var saltIng = await UsedIngredientAsync(db, "sea salt flakes", 3);
            var creamy = await UsedIngredientAsync(db, "creamery butter", 2);
            FoodCatalogProposalEntity Link(IngredientEntity ing, string source, IngredientEntity target, FoodProposalStatus status = FoodProposalStatus.Pending) => new()
            {
                Action = FoodProposalAction.Update, Batch = IngredientLinkService.Batch, IngredientId = ing.Id, Field = FoodCatalogReviewService.FdcLinkField,
                CurrentValue = ing.Name, ProposedValue = target.Id.ToString(), FdcId = target.FdcId, Source = source, Status = status,
            };
            var staple = Link(butter, IngredientLinkService.StapleSource, salted);
            var attach = new FoodCatalogProposalEntity
            {
                Action = FoodProposalAction.Update, IngredientId = honey.Id, Field = FoodCatalogReviewService.FdcAttachField,
                FdcId = "169640", Source = "fdc:169640", Status = FoodProposalStatus.Pending,
            };
            db.FoodCatalogProposals.AddRange(staple, attach,
                Link(saltIng, IngredientLinkService.ExactSource, salt),
                Link(creamy, "ai:fake:1b", plain));
            await db.SaveChangesAsync();
            var linker = Linker(db, new FakeMatcher());

            (await linker.PendingDeterministicAsync(10, Array.Empty<long>())).Should().Equal(staple.Id, attach.Id);
            (await linker.PendingDeterministicAsync(10, new[] { staple.Id })).Should().Equal(attach.Id);
            (await linker.PendingDeterministicAsync(10, Array.Empty<long>(), includeNameMatches: true)).Should().HaveCount(3, "an operator may opt exact name matches in");
        }

        [Fact]
        public async Task A_held_staple_or_verified_pick_is_left_for_a_person_and_keeps_its_hold_when_repointed()
        {
            using var db = NewContext();
            var (_, salted, plain) = await SeedFoodsAsync(db);
            var butter = await UsedIngredientAsync(db, "butter", 5);
            var unsalted = await UsedIngredientAsync(db, "creamery butter", 2);
            var held = LinkAutoApplyGuard.MarkUndone("→ Butter, without salt · used in 5 recipes");
            var staple = new FoodCatalogProposalEntity
            {
                Action = FoodProposalAction.Update, Batch = IngredientLinkService.Batch, IngredientId = butter.Id, Field = FoodCatalogReviewService.FdcLinkField,
                CurrentValue = "butter", ProposedValue = plain.Id.ToString(), FdcId = plain.FdcId, Source = "ai:fake:1b", Reason = held, Status = FoodProposalStatus.Pending,
            };
            db.FoodCatalogProposals.Add(staple);
            var verified = await AiPickAsync(db, unsalted, plain, 0.95m);
            verified.Reason = LinkAutoApplyGuard.MarkUndone(verified.Reason + IngredientLinkService.VerifiedMark);
            await db.SaveChangesAsync();
            var linker = Linker(db, new FakeMatcher());

            (await linker.ApplyStapleDefaultsToPendingAsync()).Should().Be(1);

            var repointed = await db.FoodCatalogProposals.SingleAsync(p => p.Id == staple.Id);
            repointed.Should().Match<FoodCatalogProposalEntity>(p => p.Source == IngredientLinkService.StapleSource && p.FdcId == salted.FdcId);
            repointed.Reason.Should().Be("→ Butter, salted · used in 5 recipes" + LinkAutoApplyGuard.HoldMark + LinkAutoApplyGuard.UndoneRule);
            (await linker.PendingDeterministicAsync(10, Array.Empty<long>())).Should().BeEmpty();
            (await linker.PendingVerifiedAsync(10, Array.Empty<long>())).Should().BeEmpty();
        }

        [Fact]
        public async Task Pending_name_matches_the_matcher_no_longer_makes_are_withdrawn()
        {
            using var db = NewContext();
            var (salt, _, _) = await SeedFoodsAsync(db);
            var fish = await UsedIngredientAsync(db, "fish", 6);
            db.FoodCatalogProposals.Add(new FoodCatalogProposalEntity
            {
                Action = FoodProposalAction.Update, Batch = IngredientLinkService.Batch, IngredientId = fish.Id, Field = FoodCatalogReviewService.FdcLinkField,
                CurrentValue = "fish", ProposedValue = salt.Id.ToString(), FdcId = salt.FdcId, Source = IngredientLinkService.ExactSource, Status = FoodProposalStatus.Pending,
            });
            await db.SaveChangesAsync();
            var linker = Linker(db, new FakeMatcher());

            (await linker.ApplyStapleDefaultsToPendingAsync()).Should().Be(1);

            (await db.FoodCatalogProposals.AnyAsync()).Should().BeFalse();
            (await linker.NextSourcesAsync(10)).Select(s => s.Name).Should().Equal("fish");
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

            (await review.ApplyProposalAsync(proposal.Id, reviewerPersonId: 2)).Should().BeTrue();

            cleanup.Merged.Should().Equal((saltIng.Id, salt.Id));
            cleanup.KeptProposals.Should().Equal(new long?[] { proposal.Id }, "the merge clears stale link proposals but must keep the one being applied");
            var usdaSalt = await db.Ingredients.FindAsync(salt.Id);
            usdaSalt!.CurationStatusId.Should().Be((long)CurationStatusEnum.Curated);
            usdaSalt.Name.Should().Be("Sea Salt", "the USDA food takes the everyday name recipes show");
            var kept = await db.IngredientAliases.SingleAsync();
            kept.AliasName.Should().Be("Salt, table");
            kept.SourceContext.Should().Be(IngredientLinkService.UsdaDescriptionContext, "the matcher keeps finding the food by its description");
            (await db.FoodCatalogProposals.FindAsync(proposal.Id))!.Status.Should().Be(FoodProposalStatus.Applied);
        }

        [Fact]
        public async Task A_link_the_lane_applies_on_its_own_keeps_the_usda_description_as_the_name()
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
            var review = new FoodCatalogReviewService(db, new NoAudit(), new FakeCleanup());

            (await review.ApplyProposalAsync(proposal.Id, SystemConstants.SystemPersonId)).Should().BeTrue();

            (await db.Ingredients.FindAsync(salt.Id))!.Name.Should().Be("Salt, table", "nobody has looked at the pick, so it must not rename the food");
            var alias = await db.IngredientAliases.SingleAsync();
            alias.AliasName.Should().Be("sea salt");
            alias.SourceContext.Should().Be("link:ai:fake");
        }

        private static async Task<(IngredientEntity Salt, FoodCatalogProposalEntity Proposal)> SeaSaltLinkAsync(ApplicationDbContext db, string source = "ai:fake")
        {
            var salt = new IngredientEntity { Name = "Salt", FdcId = "173468", FdcDataType = "sr_legacy_food", CurationStatusId = 9003 };
            db.Ingredients.Add(salt);
            await db.SaveChangesAsync();
            var saltIng = await UsedIngredientAsync(db, "sea salt", 1);
            var proposal = new FoodCatalogProposalEntity
            {
                Action = FoodProposalAction.Update, IngredientId = saltIng.Id, Field = FoodCatalogReviewService.FdcLinkField,
                FdcId = salt.FdcId, ProposedValue = salt.Id.ToString(), Source = source, Status = FoodProposalStatus.Pending,
            };
            db.FoodCatalogProposals.Add(proposal);
            await db.SaveChangesAsync();
            return (salt, proposal);
        }

        [Fact]
        public async Task Approving_a_link_as_a_person_marks_the_wording_alias_admin()
        {
            using var db = NewContext();
            var (salt, proposal) = await SeaSaltLinkAsync(db);
            var review = new FoodCatalogReviewService(db, new NoAudit(), new FakeCleanup());

            (await review.ApplyProposalAsync(proposal.Id, reviewerPersonId: 2)).Should().BeTrue();

            var alias = await db.IngredientAliases.SingleAsync();
            alias.Should().Match<IngredientAliasEntity>(a => a.IngredientId == salt.Id && a.AliasName == "sea salt" && a.SourceContext == "admin:link:ai:fake");
            IngredientAliasTrust.IsTrusted(alias.SourceContext).Should().BeTrue("a person looked at the wording, so a later import may follow it");
        }

        [Fact]
        public async Task A_persons_approval_upgrades_an_untrusted_alias_already_on_the_food()
        {
            using var db = NewContext();
            var (salt, proposal) = await SeaSaltLinkAsync(db);
            db.IngredientAliases.Add(new IngredientAliasEntity { IngredientId = salt.Id, AliasName = "Sea Salt" });
            await db.SaveChangesAsync();
            var review = new FoodCatalogReviewService(db, new NoAudit(), new FakeCleanup());

            (await review.ApplyProposalAsync(proposal.Id, reviewerPersonId: 2)).Should().BeTrue();

            var alias = await db.IngredientAliases.SingleAsync();
            alias.AliasName.Should().Be("Sea Salt");
            alias.SourceContext.Should().Be("admin:link:ai:fake");
            alias.LastModifiedByPersonId.Should().Be(2);
        }

        [Fact]
        public async Task A_lane_applied_link_does_not_downgrade_a_trusted_alias()
        {
            using var db = NewContext();
            var (salt, proposal) = await SeaSaltLinkAsync(db);
            db.IngredientAliases.Add(new IngredientAliasEntity { IngredientId = salt.Id, AliasName = "sea salt", SourceContext = "admin:link:ai:earlier" });
            await db.SaveChangesAsync();
            var review = new FoodCatalogReviewService(db, new NoAudit(), new FakeCleanup());

            (await review.ApplyProposalAsync(proposal.Id, SystemConstants.SystemPersonId)).Should().BeTrue();

            (await db.IngredientAliases.SingleAsync()).SourceContext.Should().Be("admin:link:ai:earlier");
        }

        [Fact]
        public async Task A_model_pick_the_lane_applies_leaves_an_alias_with_no_context_untrusted()
        {
            using var db = NewContext();
            var (salt, proposal) = await SeaSaltLinkAsync(db);
            db.IngredientAliases.Add(new IngredientAliasEntity { IngredientId = salt.Id, AliasName = "sea salt" });
            await db.SaveChangesAsync();
            var review = new FoodCatalogReviewService(db, new NoAudit(), new FakeCleanup());

            (await review.ApplyProposalAsync(proposal.Id, SystemConstants.SystemPersonId)).Should().BeTrue();

            (await db.IngredientAliases.SingleAsync()).SourceContext.Should().BeNull();
        }

        [Fact]
        public async Task A_rule_proposed_link_the_lane_applies_upgrades_an_alias_with_no_context()
        {
            using var db = NewContext();
            var (salt, proposal) = await SeaSaltLinkAsync(db, IngredientLinkService.ExactSource);
            db.IngredientAliases.Add(new IngredientAliasEntity { IngredientId = salt.Id, AliasName = "sea salt" });
            await db.SaveChangesAsync();
            var review = new FoodCatalogReviewService(db, new NoAudit(), new FakeCleanup());

            (await review.ApplyProposalAsync(proposal.Id, SystemConstants.SystemPersonId)).Should().BeTrue();

            (await db.IngredientAliases.SingleAsync()).SourceContext.Should().Be("link:deterministic:name-match");
        }

        [Fact]
        public async Task An_approved_wording_that_was_removed_from_the_food_comes_back_as_the_same_alias()
        {
            using var db = NewContext();
            var (salt, proposal) = await SeaSaltLinkAsync(db);
            db.IngredientAliases.Add(new IngredientAliasEntity { IngredientId = salt.Id, AliasName = "sea salt", SourceContext = "link:ai:earlier", IsDeleted = true, DeletedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            var review = new FoodCatalogReviewService(db, new NoAudit(), new FakeCleanup());

            (await review.ApplyProposalAsync(proposal.Id, reviewerPersonId: 2)).Should().BeTrue();

            var alias = await db.IngredientAliases.IgnoreQueryFilters().SingleAsync();
            alias.Should().Match<IngredientAliasEntity>(a => !a.IsDeleted && a.DeletedAt == null && a.SourceContext == "admin:link:ai:fake");
        }

        [Theory]
        [InlineData("Olive Oil", "Olive Oil")]
        [InlineData("baby spinach", "Baby Spinach")]
        [InlineData("diced red onion", null)]
        [InlineData("salt (or to taste", null)]
        [InlineData("2 cups flour", null)]
        [InlineData("½ cup unsalted butter", null)]
        [InlineData("A pint of milk", null)]
        [InlineData("Two eggs", null)]
        [InlineData("Yolks of raw eggs", null)]
        [InlineData("Cupful of rice", null)]
        [InlineData("Large carrots", null)]
        [InlineData("Juice of one orange", null)]
        [InlineData("Pt. of whipped cream", null)]
        [InlineData("Rosemary for garnish", null)]
        [InlineData("Cream of tartar", null)]
        [InlineData("Brown Sugar", "Brown Sugar")]
        [InlineData("green onions", "Green Onions")]
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

        [Fact]
        public async Task An_admin_entered_link_lists_ahead_of_thousands_of_model_picks()
        {
            using var db = NewContext();
            for (var i = 0; i < 30; i++)
                db.FoodCatalogProposals.Add(new FoodCatalogProposalEntity
                {
                    Action = FoodProposalAction.Update, Batch = IngredientLinkService.Batch, Field = FoodCatalogReviewService.FdcLinkField,
                    IngredientId = 100 + i, Source = "ai:fake:1b", Confidence = 1.000m, Status = FoodProposalStatus.Pending,
                });
            db.FoodCatalogProposals.Add(new FoodCatalogProposalEntity
            {
                Action = FoodProposalAction.Update, Batch = IngredientLinkService.Batch, Field = FoodCatalogReviewService.FdcLinkField,
                IngredientId = 50, Source = IngredientLinkService.StapleSource, Confidence = 0.900m, Status = FoodProposalStatus.Pending,
            });
            db.FoodCatalogProposals.Add(new FoodCatalogProposalEntity
            {
                Action = FoodProposalAction.Update, Batch = IngredientLinkService.Batch, Field = FoodCatalogReviewService.FdcLinkField,
                IngredientId = 7, Source = "admin:zucchini-relink", Confidence = 1.000m, Status = FoodProposalStatus.Pending,
            });
            await db.SaveChangesAsync();

            var listed = await new FoodCatalogReviewService(db, new NoAudit(), new FakeCleanup())
                .GetProposalsAsync(IngredientLinkService.Batch, "Pending", 10);

            listed.Select(p => p.Source).Take(2).Should().Equal("admin:zucchini-relink", IngredientLinkService.StapleSource);
        }

        [Fact]
        public async Task Approving_foods_that_are_already_curated_changes_nothing_and_says_so()
        {
            using var db = NewContext();
            var (salt, salted, _) = await SeedFoodsAsync(db);
            salt.CurationStatusId = (long)CurationStatusEnum.Curated;
            var stamped = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            salt.LastModifiedDate = stamped;
            await db.SaveChangesAsync();

            var (updated, unchanged) = await new FoodCatalogReviewService(db, new NoAudit(), new FakeCleanup())
                .SetCurationStatusAsync(new[] { salt.Id, salted.Id }, (long)CurationStatusEnum.Curated);

            (updated, unchanged).Should().Be((1, 1));
            salt.LastModifiedDate.Should().Be(stamped);
            salted.CurationStatusId.Should().Be((long)CurationStatusEnum.Curated);
        }

        private static FoodCatalogProposalEntity AppliedBySystem(IngredientEntity target, DateTime when, string source = "ai:fake:1b") => new()
        {
            Action = FoodProposalAction.Update, Batch = IngredientLinkService.Batch, IngredientId = 9000 + target.Id, Field = FoodCatalogReviewService.FdcLinkField,
            FdcId = target.FdcId, ProposedValue = target.Id.ToString(), Source = source, Status = FoodProposalStatus.Applied,
            ReviewedByPersonId = SystemConstants.SystemPersonId, ReviewedAt = when,
        };

        [Fact]
        public async Task The_lane_stops_applying_model_picks_at_its_daily_allowance_and_leaves_a_food_that_took_its_share()
        {
            using var db = NewContext();
            var (salt, salted, plain) = await SeedFoodsAsync(db);
            var now = DateTime.UtcNow;
            db.FoodCatalogProposals.AddRange(
                AppliedBySystem(salt, now.AddHours(-1)), AppliedBySystem(salt, now.AddHours(-2)), AppliedBySystem(salt, now.AddHours(-3)),
                AppliedBySystem(salted, now.AddHours(-1)),
                AppliedBySystem(plain, now.AddDays(-3)),
                AppliedBySystem(plain, now.AddHours(-1), IngredientLinkService.ExactSource));
            await db.SaveChangesAsync();
            var toSalt = await AiPickAsync(db, await UsedIngredientAsync(db, "sea salt", 2), salt, 0.95m);
            var toPlain = await AiPickAsync(db, await UsedIngredientAsync(db, "sweet butter", 2), plain, 0.95m);
            foreach (var p in new[] { toSalt, toPlain }) p.Reason += IngredientLinkService.VerifiedMark;
            await db.SaveChangesAsync();
            var linker = Linker(db, new FakeMatcher());

            var allowance = await linker.VerifiedAllowanceAsync(dailyCap: 10, perTargetCap: 3);

            allowance.Remaining.Should().Be(6, "four model picks were applied by the lane in the last day; older ones and name matches do not count");
            allowance.BusyTargets.Should().BeEquivalentTo(new[] { salt.FdcId });
            (await linker.PendingVerifiedAsync(10, Array.Empty<long>(), 0.85m, default, allowance.BusyTargets)).Should().BeEquivalentTo(new[] { toPlain.Id });
            (await linker.VerifiedAllowanceAsync(dailyCap: 4, perTargetCap: 3)).Remaining.Should().Be(0);
        }

        private const long Reviewer = 2;

        private static async Task<(IngredientEntity Salt, IngredientEntity ButterSalted, IngredientEntity ButterPlain, IngredientEntity Carrots, IngredientEntity YellowOnion)> SeedAliasedFoodsAsync(ApplicationDbContext db)
        {
            var (salt, salted, plain) = await SeedFoodsAsync(db);
            var carrots = new IngredientEntity { Name = "Carrots, raw", FdcId = "170393", FdcDataType = "sr_legacy_food", CurationStatusId = 9003 };
            var yellowOnion = new IngredientEntity { Name = "Yellow Onion", CurationStatusId = 9003 };
            db.Ingredients.AddRange(carrots, yellowOnion);
            await db.SaveChangesAsync();
            return (salt, salted, plain, carrots, yellowOnion);
        }

        private static async Task AliasAsync(ApplicationDbContext db, IngredientEntity food, string wording, string? context = null, bool deleted = false)
        {
            db.IngredientAliases.Add(new IngredientAliasEntity { IngredientId = food.Id, AliasName = wording, SourceContext = context, IsDeleted = deleted });
            await db.SaveChangesAsync();
        }

        private static async Task<string?> ContextOfAsync(ApplicationDbContext db, IngredientEntity food, string wording) =>
            (await db.IngredientAliases.AsNoTracking().SingleAsync(a => a.IngredientId == food.Id && a.AliasName == wording)).SourceContext;

        [Fact]
        public async Task Classification_dry_run_counts_and_changes_nothing()
        {
            using var db = NewContext();
            var (salt, salted, plain, carrots, yellowOnion) = await SeedAliasedFoodsAsync(db);
            await AliasAsync(db, yellowOnion, "yellow onions");
            await AliasAsync(db, yellowOnion, "diced yellow onion");
            await AliasAsync(db, salted, "salted butter");
            await AliasAsync(db, carrots, "chopped carrots");
            await AliasAsync(db, plain, "butter");
            await AliasAsync(db, salt, "kosher salt");
            await AliasAsync(db, salt, "fine salt", "link:ai:fake:1b");
            await AliasAsync(db, salt, "table salt", deleted: true);

            var result = await Linker(db, new FakeMatcher()).ClassifyAliasesAsync(apply: false, Reviewer);

            result.Should().BeEquivalentTo(new { Applied = false, Examined = 6, NameKey = 2, Staple = 1, NameMatch = 1, Conflicts = 1, Unchanged = 1 });
            result.Samples.Select(s => (s.FoodName, s.AliasName, s.Rule)).Should().BeEquivalentTo(new[]
            {
                ("Yellow Onion", "yellow onions", IngredientLinkService.NameKeyRule),
                ("Yellow Onion", "diced yellow onion", IngredientLinkService.NameKeyRule),
                ("Butter, salted", "salted butter", IngredientLinkService.StapleRule),
                ("Carrots, raw", "chopped carrots", IngredientLinkService.NameMatchRule),
            });
            (await db.IngredientAliases.CountAsync(a => a.SourceContext == null)).Should().Be(6);
            (await db.AuditLogEntries.AnyAsync()).Should().BeFalse();
        }

        [Fact]
        public async Task Classification_never_trusts_a_utensil_or_an_each_of_list()
        {
            using var db = NewContext();
            var (_, _, _, carrots, _) = await SeedAliasedFoodsAsync(db);
            var jellies = new IngredientEntity { Name = "jellies", FdcId = "169642", FdcDataType = "sr_legacy_food", CurationStatusId = 9003 };
            var cornmeal = new IngredientEntity { Name = "Fine Yellow Cornmeal", FdcId = "790276", FdcDataType = "foundation_food", CurationStatusId = 9003 };
            db.Ingredients.AddRange(jellies, cornmeal);
            await db.SaveChangesAsync();
            await AliasAsync(db, jellies, "Jellies", IngredientLinkService.UsdaDescriptionContext);
            await AliasAsync(db, cornmeal, "Flour, corn, yellow, fine meal, enriched", IngredientLinkService.UsdaDescriptionContext);
            await AliasAsync(db, jellies, "jelly-bag");
            await AliasAsync(db, cornmeal, "each of corn meal and flour");
            await AliasAsync(db, carrots, "chopped carrots");

            var result = await Linker(db, new FakeMatcher()).ClassifyAliasesAsync(apply: true, Reviewer);

            (result.NameKey + result.Staple + result.NameMatch).Should().Be(1);
            (await ContextOfAsync(db, jellies, "jelly-bag")).Should().BeNull();
            (await ContextOfAsync(db, cornmeal, "each of corn meal and flour")).Should().BeNull();
            (await ContextOfAsync(db, carrots, "chopped carrots")).Should().NotBeNull();
        }

        [Fact]
        public async Task Classification_never_trusts_an_alias_made_of_a_measure_named_after_a_drink_or_a_food()
        {
            using var db = NewContext();
            var (salt, _, _, carrots, _) = await SeedAliasedFoodsAsync(db);
            await AliasAsync(db, salt, "wine glassful of salt");
            await AliasAsync(db, salt, "a teacupful of salt");
            await AliasAsync(db, carrots, "chopped carrots");
            await AliasAsync(db, carrots, "wineglass of chopped carrots");

            var result = await Linker(db, new FakeMatcher()).ClassifyAliasesAsync(apply: true, Reviewer);

            (await ContextOfAsync(db, salt, "wine glassful of salt")).Should().BeNull("staging checkpoint 4, P5: a trusted alias made of a wine glass put Salt under %wine%");
            (await ContextOfAsync(db, salt, "a teacupful of salt")).Should().BeNull();
            (await ContextOfAsync(db, carrots, "wineglass of chopped carrots")).Should().BeNull();
            (await ContextOfAsync(db, carrots, "chopped carrots")).Should().NotBeNull();
            (result.NameKey + result.Staple + result.NameMatch).Should().Be(1);
        }

        [Fact]
        public async Task Classification_samples_stop_at_the_sample_size_for_each_rule()
        {
            using var db = NewContext();
            var (_, _, _, carrots, yellowOnion) = await SeedAliasedFoodsAsync(db);
            await AliasAsync(db, yellowOnion, "yellow onions");
            await AliasAsync(db, yellowOnion, "diced yellow onion");
            await AliasAsync(db, carrots, "chopped carrots");

            var result = await Linker(db, new FakeMatcher()).ClassifyAliasesAsync(apply: false, Reviewer, sampleSize: 1);

            result.NameKey.Should().Be(2);
            result.Samples.Select(s => s.Rule).Should().BeEquivalentTo(new[] { IngredientLinkService.NameKeyRule, IngredientLinkService.NameMatchRule });
        }

        [Fact]
        public async Task Classification_marks_an_alias_that_is_the_foods_name_in_the_plural()
        {
            using var db = NewContext();
            var (_, _, _, _, yellowOnion) = await SeedAliasedFoodsAsync(db);
            await AliasAsync(db, yellowOnion, "yellow onions");
            await AliasAsync(db, yellowOnion, "1 large yellow onion (diced)");

            var result = await Linker(db, new FakeMatcher()).ClassifyAliasesAsync(apply: true, Reviewer);

            result.Applied.Should().BeTrue();
            result.NameKey.Should().Be(2);
            (await ContextOfAsync(db, yellowOnion, "yellow onions")).Should().Be("deterministic:recheck:name-key");
            (await ContextOfAsync(db, yellowOnion, "1 large yellow onion (diced)")).Should().Be("deterministic:recheck:name-key", "the name is what is left once the amount and the preparation words are off");
        }

        [Fact]
        public async Task Classification_marks_a_staple_wording_on_its_staple_food()
        {
            using var db = NewContext();
            var (_, salted, _, _, _) = await SeedAliasedFoodsAsync(db);
            salted.Name = "Butter";
            await AliasAsync(db, salted, "Butter, salted", IngredientLinkService.UsdaDescriptionContext);
            await AliasAsync(db, salted, "salted butter");

            await Linker(db, new FakeMatcher()).ClassifyAliasesAsync(apply: true, Reviewer);

            (await ContextOfAsync(db, salted, "salted butter")).Should().Be("deterministic:recheck:staple");
            (await ContextOfAsync(db, salted, "Butter, salted")).Should().Be(IngredientLinkService.UsdaDescriptionContext);
        }

        [Fact]
        public async Task Classification_marks_a_wording_the_name_matcher_gives_to_that_food()
        {
            using var db = NewContext();
            var (_, _, _, carrots, _) = await SeedAliasedFoodsAsync(db);
            await AliasAsync(db, carrots, "chopped carrots");

            await Linker(db, new FakeMatcher()).ClassifyAliasesAsync(apply: true, Reviewer);

            (await ContextOfAsync(db, carrots, "chopped carrots")).Should().Be("deterministic:recheck:name-match");
        }

        [Fact]
        public async Task Classification_leaves_an_alias_the_rules_give_to_another_food()
        {
            using var db = NewContext();
            var (_, _, plain, _, yellowOnion) = await SeedAliasedFoodsAsync(db);
            await AliasAsync(db, plain, "butter");
            await AliasAsync(db, yellowOnion, "carrots");

            var result = await Linker(db, new FakeMatcher()).ClassifyAliasesAsync(apply: true, Reviewer);

            result.Conflicts.Should().Be(2, "butter is the salted staple, and the matcher gives carrots to the carrot");
            (await ContextOfAsync(db, plain, "butter")).Should().BeNull();
            (await ContextOfAsync(db, yellowOnion, "carrots")).Should().BeNull();
            (await db.AuditLogEntries.AnyAsync()).Should().BeFalse();
        }

        [Fact]
        public async Task Classification_leaves_an_alias_no_rule_covers()
        {
            using var db = NewContext();
            var (salt, _, _, _, yellowOnion) = await SeedAliasedFoodsAsync(db);
            await AliasAsync(db, salt, "kosher salt");
            await AliasAsync(db, yellowOnion, "crispy fried onions");

            var result = await Linker(db, new FakeMatcher()).ClassifyAliasesAsync(apply: true, Reviewer);

            result.Should().BeEquivalentTo(new { Applied = true, Examined = 2, Unchanged = 2, Conflicts = 0 });
            (await db.IngredientAliases.CountAsync(a => a.SourceContext == null)).Should().Be(2);
        }

        [Fact]
        public async Task Classification_leaves_aliases_on_a_soft_deleted_food_alone()
        {
            using var db = NewContext();
            var gone = new IngredientEntity { Name = "Turnip", CurationStatusId = 9003, IsDeleted = true };
            db.Ingredients.Add(gone);
            await db.SaveChangesAsync();
            await AliasAsync(db, gone, "turnips");

            var result = await Linker(db, new FakeMatcher()).ClassifyAliasesAsync(apply: true, Reviewer);

            result.Examined.Should().Be(0);
            (await ContextOfAsync(db, gone, "turnips")).Should().BeNull();
        }

        [Fact]
        public async Task Classification_writes_one_journal_entry_per_alias_and_undo_since_restores_null_and_removes_the_entries()
        {
            using var db = NewContext();
            var (salt, salted, _, carrots, yellowOnion) = await SeedAliasedFoodsAsync(db);
            await AliasAsync(db, yellowOnion, "yellow onions");
            await AliasAsync(db, salted, "salted butter");
            await AliasAsync(db, carrots, "chopped carrots");
            await AliasAsync(db, salt, "kosher salt");
            var linker = Linker(db, new FakeMatcher());
            var started = DateTime.UtcNow;

            await linker.ClassifyAliasesAsync(apply: true, Reviewer);

            var journal = await db.AuditLogEntries.AsNoTracking().ToListAsync();
            journal.Should().OnlyContain(e => e.EntityType == IngredientLinkService.AliasEntityType && e.ChangeType == IngredientLinkService.AliasTrustChangeType
                && e.PropertyName == nameof(IngredientAliasEntity.SourceContext) && e.ChangedByPersonId == Reviewer && e.Timestamp >= started);
            journal.Select(e => (e.EntityId, e.OldValue, e.NewValue)).Should().BeEquivalentTo(new[]
            {
                (yellowOnion.Id, (string?)"yellow onions", (string?)"deterministic:recheck:name-key"),
                (salted.Id, "salted butter", "deterministic:recheck:staple"),
                (carrots.Id, "chopped carrots", "deterministic:recheck:name-match"),
            });

            var again = await linker.ClassifyAliasesAsync(apply: true, Reviewer);
            again.Should().BeEquivalentTo(new { Examined = 1, NameKey = 0, Staple = 0, NameMatch = 0, Unchanged = 1 }, "a marked alias is not looked at again");
            (await db.AuditLogEntries.CountAsync()).Should().Be(3);

            (await linker.UndoAliasClassificationAsync(DateTime.UtcNow.AddMinutes(1), Reviewer)).Should().BeEquivalentTo(new AliasClassifyUndoResult(), "nothing was marked that late");
            var undone = await linker.UndoAliasClassificationAsync(started, Reviewer);

            undone.Should().BeEquivalentTo(new AliasClassifyUndoResult { Restored = 3 });
            (await db.IngredientAliases.CountAsync(a => a.SourceContext == null)).Should().Be(4);
            (await db.AuditLogEntries.AnyAsync()).Should().BeFalse();
        }

        [Fact]
        public async Task Undo_skips_an_alias_whose_context_changed_since()
        {
            using var db = NewContext();
            var (_, salted, _, carrots, yellowOnion) = await SeedAliasedFoodsAsync(db);
            await AliasAsync(db, yellowOnion, "yellow onions");
            await AliasAsync(db, salted, "salted butter");
            await AliasAsync(db, carrots, "chopped carrots");
            var linker = Linker(db, new FakeMatcher());
            var started = DateTime.UtcNow;
            await linker.ClassifyAliasesAsync(apply: true, Reviewer);
            var approved = await db.IngredientAliases.SingleAsync(a => a.AliasName == "yellow onions");
            approved.SourceContext = "admin:link:ai:fake";
            db.IngredientAliases.Remove(await db.IngredientAliases.SingleAsync(a => a.AliasName == "chopped carrots"));
            await db.SaveChangesAsync();

            var undone = await linker.UndoAliasClassificationAsync(started, Reviewer);

            undone.Should().BeEquivalentTo(new AliasClassifyUndoResult { Restored = 1, Skipped = 2 });
            (await ContextOfAsync(db, yellowOnion, "yellow onions")).Should().Be("admin:link:ai:fake");
            (await ContextOfAsync(db, salted, "salted butter")).Should().BeNull();
            (await db.AuditLogEntries.Select(e => e.OldValue).ToListAsync()).Should().BeEquivalentTo(new[] { "yellow onions", "chopped carrots" }, "a skipped entry stays in the journal");
        }

        [Fact]
        public async Task A_person_approving_a_wording_after_classification_keeps_it_trusted_through_the_classify_undo()
        {
            using var db = NewContext();
            var (_, _, _, carrots, _) = await SeedAliasedFoodsAsync(db);
            await AliasAsync(db, carrots, "chopped carrots");
            var linker = Linker(db, new FakeMatcher());
            var started = DateTime.UtcNow;
            await linker.ClassifyAliasesAsync(apply: true, Reviewer);
            (await ContextOfAsync(db, carrots, "chopped carrots")).Should().Be("deterministic:recheck:name-match");
            var wording = new IngredientEntity { Name = "chopped carrots", CurationStatusId = 9000 };
            db.Ingredients.Add(wording);
            await db.SaveChangesAsync();
            var proposal = new FoodCatalogProposalEntity
            {
                Action = FoodProposalAction.Update, IngredientId = wording.Id, Field = FoodCatalogReviewService.FdcLinkField, CurrentValue = wording.Name,
                FdcId = carrots.FdcId, ProposedValue = carrots.Id.ToString(), Source = "ai:fake", Batch = IngredientLinkService.Batch, Status = FoodProposalStatus.Pending,
            };
            db.FoodCatalogProposals.Add(proposal);
            await db.SaveChangesAsync();

            (await new FoodCatalogReviewService(db, new NoAudit(), new FakeCleanup()).ApplyProposalAsync(proposal.Id, Reviewer)).Should().BeTrue();
            var undone = await linker.UndoAliasClassificationAsync(started, Reviewer);

            undone.Should().BeEquivalentTo(new AliasClassifyUndoResult { Skipped = 1 });
            (await ContextOfAsync(db, carrots, "chopped carrots")).Should().Be("admin:link:ai:fake");
        }

        [Fact]
        public async Task A_wording_alias_carrying_a_staple_description_does_not_become_the_staple_target()
        {
            using var db = NewContext();
            var margarine = new IngredientEntity { Name = "Margarine, regular, 80% fat composite, stick, with salt", FdcId = "173577", FdcDataType = "sr_legacy_food", CurationStatusId = 9003 };
            db.Ingredients.Add(margarine);
            await db.SaveChangesAsync();
            await AliasAsync(db, margarine, "Butter, salted", "link:ai:fake:1b");
            var butter = await UsedIngredientAsync(db, "Butter", 5);
            var linker = Linker(db, new FakeMatcher());

            await linker.ProposeAsync(await linker.NextSourcesAsync(10));

            var proposal = await db.FoodCatalogProposals.SingleAsync(p => p.IngredientId == butter.Id);
            proposal.Source.Should().NotBe(IngredientLinkService.StapleSource, "only a food's own USDA description makes it a staple's standard entry");
            proposal.ProposedValue.Should().BeNull();
        }
    }
}

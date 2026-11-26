using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Nom.Data;
using Nom.Data.Curation;
using Nom.Data.Nutrient;
using Nom.Data.Nutrition;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Curation;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// The second re-check fix round of staging checkpoint 3 (N12, N18, N21): a class below the bar
    /// writes nothing and hands its lines to the model audit as shadow candidates; a move gives no
    /// alias that names a restricted food its new food does not ("corn brandy" on distilled
    /// spirits); an undo leaves no deleted copy of an alias its run created, under a request as on
    /// staging; and a prepared food holding a wording's very name takes a name that differs from
    /// the wording beyond case.
    /// </summary>
    public class LinkRecheckClassBarTests
    {
        private sealed class NoUser : ICurrentUserService
        {
            public long? PersonId => null;
            public long PersonIdOrSystem => SystemConstants.SystemPersonId;
            public long RequiredPersonId => throw new UnauthorizedAccessException();
            public string? UserId => null;
            public string RequiredUserId => throw new UnauthorizedAccessException();
        }

        private static ApplicationDbContext NewContext(bool request = false)
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;
            return request ? new ApplicationDbContext(options, new HttpContextAccessor { HttpContext = new DefaultHttpContext() }) : new ApplicationDbContext(options);
        }

        private static LinkRecheckService Recheck(ApplicationDbContext db) =>
            new(db, new NoUser(), new LinkAutoApplyService(db, new MemoryCache(new MemoryCacheOptions())), new LineRelinkService(db, new NoUser()),
                new LaneFrameService(db, Array.Empty<ILaneUndo>(), NullLogger<LaneFrameService>.Instance), NullLogger<LinkRecheckService>.Instance);

        private static IngredientEntity Food(string name, string? description = null, string? fdcId = null, int? category = null)
        {
            var food = new IngredientEntity
            {
                Name = name,
                CurationStatusId = (long)CurationStatusEnum.Curated,
                FdcId = fdcId,
                FdcCategoryId = category,
                FdcDataType = fdcId == null ? string.Empty : CatalogAdmissionRule.SrLegacyFood,
                IngredientNutrients = new List<IngredientNutrientEntity> { new() { NutrientId = 1, Amount = 1 } },
            };
            if (description != null) food.Aliases.Add(new IngredientAliasEntity { AliasName = description, SourceContext = IngredientLinkService.UsdaDescriptionContext });
            return food;
        }

        private static RecipeEntity Scraped(string name, params (IngredientEntity Food, string Line)[] lines) => new()
        {
            Name = name,
            ScrapedAtUtc = DateTime.UtcNow,
            RecipeIngredients = lines.Select((l, i) => new RecipeIngredientEntity
            {
                IngredientId = l.Food.Id, SortOrder = i, Quantity = 1, MeasurementId = 1, RawLine = l.Line,
            }).ToList(),
        };

        private static async Task<long> FoodOfAsync(ApplicationDbContext db, long recipeId, string rawLine)
        {
            db.ChangeTracker.Clear();
            return await db.RecipeIngredients.AsNoTracking().Where(ri => ri.RecipeId == recipeId && ri.RawLine == rawLine).Select(ri => ri.IngredientId).SingleAsync();
        }

        internal static void Live(ApplicationDbContext db, params string[] tiers)
        {
            foreach (var tier in tiers.Where(t => !db.LaneStates.Any(s => s.Lane == LinkRecheckService.Lane && s.Tier == t)))
                db.LaneStates.Add(new LaneStateEntity { Lane = LinkRecheckService.Lane, Tier = tier, Mode = LaneMode.Live, SignatureHash = LinkRecheckRules.TierSignature(tier), Since = DateTime.UtcNow, WindowStart = DateTime.UtcNow });
            db.SaveChanges();
            db.ChangeTracker.Clear();
        }

        internal static Task LiveAsync(ApplicationDbContext db, params string[] tiers)
        {
            Live(db, tiers);
            return Task.CompletedTask;
        }

        internal static void Qualify(ApplicationDbContext db, params string[] classes) => Live(db, classes.Select(LinkRecheckRules.CandidateTier).ToArray());

        internal static Task QualifyAsync(ApplicationDbContext db, params string[] classes) => LiveAsync(db, classes.Select(LinkRecheckRules.CandidateTier).ToArray());

        private static async Task<List<string>> AliasRowsAsync(ApplicationDbContext db)
        {
            db.ChangeTracker.Clear();
            return await db.IngredientAliases.IgnoreQueryFilters().AsNoTracking().OrderBy(a => a.IngredientId).ThenBy(a => a.AliasName)
                .Select(a => $"{a.IngredientId}:{a.AliasName}:{a.SourceContext}:{a.IsDeleted}").ToListAsync();
        }

        [Fact]
        public async Task An_operator_run_keeps_the_lines_of_a_class_not_proven_and_records_them_in_shadow_for_the_model_audit()
        {
            await using var db = NewContext();
            var skins = Food("Raw Potato Skins", "Potatoes, raw, skin", "170032", 11);
            var potatoes = Food("Potatoes", "Potatoes, flesh and skin, raw", "170026", 11);
            db.Ingredients.AddRange(skins, potatoes);
            await db.SaveChangesAsync();
            db.IngredientAliases.Add(new IngredientAliasEntity { IngredientId = skins.Id, AliasName = "large potatoes", SourceContext = IngredientAliasTrust.RecheckPrefix + "name-match" });
            var soup = Scraped("Potato soup", (skins, "4 large potatoes"));
            db.Recipes.Add(soup);
            await db.SaveChangesAsync();

            var run = await Recheck(db).RunAsync(LinkRecheckRules.PartHerb, apply: true);

            (await FoodOfAsync(db, soup.Id, "4 large potatoes")).Should().Be(skins.Id, "the part class has not held the bar on the checkpoints' judged lines");
            (run!.LinesMoved, run.LinesUnlinked, run.Candidates, run.CandidateLines).Should().Be((0, 0, 1, 1));
            var decision = await db.LaneDecisions.AsNoTracking().SingleAsync();
            (decision.Lane, decision.Tier, decision.Mode, decision.Writes, decision.Stratum).Should().Be((LinkRecheckService.Lane, LinkRecheckRules.CandidateTier(LinkGate.Part), LaneMode.Shadow, true, LinkRecheckRules.PartHerb),
                "the candidate is recorded on its class's own tier as a decision that would write, in shadow, so the frame's auditor samples that class apart from the set");
            var signals = LaneJournal.Read<LinkRecheckService.LinkRecheckSignals>(decision.SignalsJson)!;
            (signals.Outcome, signals.Class, signals.TargetId).Should().Be((LinkRecheckRules.Move, LinkGate.Part, potatoes.Id));
            (await Recheck(db).StatusAsync(false)).ProvenClasses.Should().BeEquivalentTo(LinkRecheckRules.ProvenClasses);
        }

        [Fact]
        public async Task A_live_set_does_not_write_the_candidates_of_a_class_whose_own_tier_has_not_qualified()
        {
            await using var db = NewContext();
            var skins = Food("Raw Potato Skins", "Potatoes, raw, skin", "170032", 11);
            var potatoes = Food("Potatoes", "Potatoes, flesh and skin, raw", "170026", 11);
            db.Ingredients.AddRange(skins, potatoes);
            await db.SaveChangesAsync();
            db.IngredientAliases.Add(new IngredientAliasEntity { IngredientId = skins.Id, AliasName = "large potatoes", SourceContext = IngredientAliasTrust.RecheckPrefix + "name-match" });
            var soup = Scraped("Potato soup", (skins, "4 large potatoes"));
            db.Recipes.Add(soup);
            await db.SaveChangesAsync();
            await LiveAsync(db, LinkRecheckRules.PartHerb);

            var live = await Recheck(db).RunLaneBatchAsync(LinkRecheckRules.PartHerb, LaneMode.Live, 100);

            (await FoodOfAsync(db, soup.Id, "4 large potatoes")).Should().Be(skins.Id,
                "the set's tier qualified on its own would-write decisions, but the part class has not qualified on its own candidates");
            (live.LinesMoved, live.Candidates, live.ByQualified.Count).Should().Be((0, 1, 0));
            (await db.LaneDecisions.AsNoTracking().SingleAsync()).Tier.Should().Be(LinkRecheckRules.CandidateTier(LinkGate.Part));
        }

        [Fact]
        public async Task A_class_tier_that_qualified_writes_its_candidates_as_a_run_of_its_own_which_its_pause_undoes()
        {
            await using var db = NewContext();
            var skins = Food("Raw Potato Skins", "Potatoes, raw, skin", "170032", 11);
            var potatoes = Food("Potatoes", "Potatoes, flesh and skin, raw", "170026", 11);
            db.Ingredients.AddRange(skins, potatoes);
            await db.SaveChangesAsync();
            db.IngredientAliases.Add(new IngredientAliasEntity { IngredientId = skins.Id, AliasName = "large potatoes", SourceContext = IngredientAliasTrust.RecheckPrefix + "name-match" });
            var soup = Scraped("Potato soup", (skins, "4 large potatoes"));
            db.Recipes.Add(soup);
            await db.SaveChangesAsync();
            await QualifyAsync(db, LinkGate.Part);
            var service = Recheck(db);

            var live = await service.RunLaneBatchAsync(LinkRecheckRules.PartHerb, LaneMode.Shadow, 100);

            (await FoodOfAsync(db, soup.Id, "4 large potatoes")).Should().Be(potatoes.Id, "the part tier is live, whatever the set's own mode");
            live.ByQualified.Should().Equal(new Dictionary<string, int> { [LinkGate.Part] = 1 });
            var decision = await db.LaneDecisions.AsNoTracking().SingleAsync();
            (decision.Tier, decision.Mode, decision.Written).Should().Be((LinkRecheckRules.CandidateTier(LinkGate.Part), LaneMode.Live, true));
            var undo = await new LinkRecheckLaneUndo(service, db).UndoSinceAsync(LinkRecheckRules.CandidateTier(LinkGate.Part), DateTime.UtcNow.AddHours(-1), apply: true, default);
            undo.Undone.Should().BeGreaterThan(0, "the class's run is journaled under its own tier, so the tier's pause takes it back");
            (await FoodOfAsync(db, soup.Id, "4 large potatoes")).Should().Be(skins.Id);
        }

        [Fact]
        public async Task A_candidate_an_operator_recorded_in_shadow_is_decided_again_by_the_lane_once_its_class_qualifies()
        {
            await using var db = NewContext();
            var skins = Food("Raw Potato Skins", "Potatoes, raw, skin", "170032", 11);
            var potatoes = Food("Potatoes", "Potatoes, flesh and skin, raw", "170026", 11);
            db.Ingredients.AddRange(skins, potatoes);
            await db.SaveChangesAsync();
            db.IngredientAliases.Add(new IngredientAliasEntity { IngredientId = skins.Id, AliasName = "large potatoes", SourceContext = IngredientAliasTrust.RecheckPrefix + "name-match" });
            var soup = Scraped("Potato soup", (skins, "4 large potatoes"));
            db.Recipes.Add(soup);
            await db.SaveChangesAsync();
            var service = Recheck(db);

            await service.RunAsync(LinkRecheckRules.PartHerb, apply: true);
            (await service.RunLaneBatchAsync(LinkRecheckRules.PartHerb, LaneMode.Shadow, 100)).Groups.Should().Be(0, "a candidate already recorded in shadow is not recorded again while its class is in shadow");

            await QualifyAsync(db, LinkGate.Part);
            var live = await service.RunLaneBatchAsync(LinkRecheckRules.PartHerb, LaneMode.Shadow, 100);

            live.Groups.Should().Be(1, "the operator's shadow record does not hold the wording back from the lane for 30 days once its class may write");
            (await FoodOfAsync(db, soup.Id, "4 large potatoes")).Should().Be(potatoes.Id);
        }

        [Fact]
        public async Task The_alcohol_set_moves_corn_brandy_to_the_spirit_but_gives_the_spirit_no_alias_naming_corn()
        {
            await using var db = NewContext();
            var liqueur = Food("Coffee Cream Liqueur", "Alcoholic beverage, liqueur, coffee with cream, 34 proof", "175098", 14);
            var spirit = Food("Distilled Spirit", "Alcoholic beverage, distilled, all (gin, rum, vodka, whiskey) 80 proof", "174815", 14);
            db.Ingredients.AddRange(liqueur, spirit);
            await db.SaveChangesAsync();
            db.IngredientAliases.Add(new IngredientAliasEntity { IngredientId = liqueur.Id, AliasName = "corn brandy", SourceContext = "link:ai:qwen2.5:14b-instruct" });
            var punch = Scraped("Punch", (liqueur, "1 pint corn brandy"));
            db.Recipes.Add(punch);
            await db.SaveChangesAsync();
            Qualify(db, LinkRecheckRules.AlcoholClass);
            var service = Recheck(db);

            var dry = await service.RunAsync(LinkRecheckRules.Alcohol);
            var run = await service.RunAsync(LinkRecheckRules.Alcohol, apply: true);

            (await FoodOfAsync(db, punch.Id, "1 pint corn brandy")).Should().Be(spirit.Id, "the line is a spirit, which the sense table allows");
            (await db.IngredientAliases.AnyAsync(a => a.IngredientId == spirit.Id && a.AliasName == "corn brandy"))
                .Should().BeFalse("an alias naming corn on distilled spirits would make Corn Allergy keep out every brandy recipe");
            (run!.AliasesRefused, run.AliasesAdded + run.AliasesMoved, run.AliasesRemoved).Should().Be((1, 0, 1));
            dry!.AliasesRefused.Should().Be(run.AliasesRefused);
        }

        [Fact]
        public async Task A_set_undo_under_a_request_leaves_no_deleted_copy_of_an_alias_its_run_created_and_a_reapply_writes_the_same_rows()
        {
            await using var db = NewContext(request: true);
            var vinegar = Food("Red Wine Vinegar", "Vinegar, red wine", "172241", 2);
            var dry = Food("Dessert Wine, Dry", "Alcoholic beverage, wine, dessert, dry", "175112", 14);
            db.Ingredients.AddRange(vinegar, dry);
            await db.SaveChangesAsync();
            db.IngredientAliases.Add(new IngredientAliasEntity { IngredientId = vinegar.Id, AliasName = "dry sherry", SourceContext = "link:ai:qwen2.5:14b-instruct" });
            var trifle = Scraped("Trifle", (vinegar, "1/2 cup dry sherry"));
            db.Recipes.Add(trifle);
            await db.SaveChangesAsync();
            Qualify(db, LinkRecheckRules.AlcoholClass);
            var service = Recheck(db);
            var before = await AliasRowsAsync(db);

            var run = await service.RunAsync(LinkRecheckRules.Alcohol, apply: true);
            (await FoodOfAsync(db, trifle.Id, "1/2 cup dry sherry")).Should().Be(dry.Id);
            (run!.AliasesAdded + run.AliasesMoved).Should().Be(1, "dry sherry is the dry dessert wine to the gate, so the move may give it an alias");
            var applied = await AliasRowsAsync(db);

            await service.UndoAsync(run.Stamp!.Value, apply: true);
            (await AliasRowsAsync(db)).Should().Equal(before, "an alias the run created is deleted as a row, not left behind as a deleted copy");

            await service.RunAsync(LinkRecheckRules.Alcohol, apply: true);
            (await AliasRowsAsync(db)).Should().Equal(applied, "a re-apply after the undo writes the same alias rows");
        }

        [Fact]
        public async Task Two_aliases_of_one_wording_on_a_food_are_both_taken_and_both_given_back()
        {
            await using var db = NewContext(request: true);
            var thighs = Food("Breaded Chicken Thighs", "Chicken, thighs, frozen, breaded, reheated", "171090", 22);
            db.Ingredients.Add(thighs);
            await db.SaveChangesAsync();
            db.IngredientAliases.AddRange(
                new IngredientAliasEntity { IngredientId = thighs.Id, AliasName = "chicken thighs", SourceContext = "link:ai:qwen2.5:14b-instruct" },
                new IngredientAliasEntity { IngredientId = thighs.Id, AliasName = "Chicken thighs" });
            var dinner = Scraped("Dinner", (thighs, "6 chicken thighs"));
            db.Recipes.Add(dinner);
            await db.SaveChangesAsync();
            Qualify(db, LinkGate.Prepared);
            var service = Recheck(db);
            var before = await AliasRowsAsync(db);

            var first = await service.RunAsync(LinkRecheckRules.Prepared, apply: true);
            var applied = await AliasRowsAsync(db);
            first!.AliasesRemoved.Should().Be(2, "every alias of the wording a model or no one gave goes with the lines (one of them alone went on staging: the run kept the aliases it had taken by an id the alias table does not have)");
            await service.UndoAsync(first.Stamp!.Value, apply: true, restoreLowered: true);
            (await AliasRowsAsync(db)).Should().Equal(before);
            var second = await service.RunAsync(LinkRecheckRules.Prepared, apply: true);
            (await AliasRowsAsync(db)).Should().Equal(applied, "the same aliases are taken each time");
            second!.AliasesRemoved.Should().Be(2);
        }

        [Fact]
        public async Task A_prepared_food_whose_description_is_the_wording_in_another_case_takes_a_name_that_differs_beyond_case()
        {
            await using var db = NewContext();
            var zwieback = Food("zwieback", "Zwieback", "171372", 3);
            db.Ingredients.Add(zwieback);
            await db.SaveChangesAsync();
            var nursery = Scraped("Nursery", (zwieback, "6 zwieback"));
            db.Recipes.Add(nursery);
            await db.SaveChangesAsync();
            Qualify(db, LinkGate.Prepared);
            var service = Recheck(db);

            var run = await service.RunAsync(LinkRecheckRules.Prepared, apply: true);

            run!.FoodsRenamed.Should().Be(1);
            (await db.Ingredients.AsNoTracking().SingleAsync(i => i.Id == zwieback.Id)).Name.Should().Be("Zwieback (Baby Foods)");
            var home = await FoodOfAsync(db, nursery.Id, "6 zwieback");
            (await db.Ingredients.AsNoTracking().SingleAsync(i => i.Id == home)).Name.Should().Be("zwieback");
            await service.UndoAsync(run.Stamp!.Value, apply: true);
            (await db.Ingredients.AsNoTracking().SingleAsync(i => i.Id == zwieback.Id)).Name.Should().Be("zwieback");
        }

        [Fact]
        public async Task A_class_the_committed_record_proves_writes_by_rule_with_no_tier_of_its_own_qualified()
        {
            LinkRecheckRules.ProvenClasses.Should().Contain(LinkGate.Prepared, "the checkpoint-judged lines of link-recheck.tsv hold the bar for prepared foods (scores/link-recheck-classes.json)");
            await using var db = NewContext();
            var thighs = Food("Breaded Chicken Thighs", "Chicken, thighs, frozen, breaded, reheated", "171090", 22);
            db.Ingredients.Add(thighs);
            await db.SaveChangesAsync();
            var dinner = Scraped("Dinner", (thighs, "6 chicken thighs"));
            db.Recipes.Add(dinner);
            await db.SaveChangesAsync();
            var service = Recheck(db);

            var run = await service.RunAsync(LinkRecheckRules.Prepared, apply: true);

            (run!.LinesUnlinked, run.Candidates).Should().Be((1, 0));
            run.ByClass.Should().ContainKey(LinkGate.Prepared);
            (await FoodOfAsync(db, dinner.Id, "6 chicken thighs")).Should().NotBe(thighs.Id);
            (await db.LaneDecisions.AsNoTracking().Where(d => d.Tier == LinkRecheckRules.CandidateTier(LinkGate.Prepared)).CountAsync()).Should().Be(0, "a proven class decides on the set's tier, not as a candidate");
            (await Recheck(db).StatusAsync(false)).Should().Match<LinkRecheckStatusModel>(s => s.ProvenClasses.Contains(LinkGate.Prepared) && s.ProvenProblem == null);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Nom.Data;
using Nom.Data.Audit;
using Nom.Data.Curation;
using Nom.Data.Nutrient;
using Nom.Data.Nutrition;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Curation;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;
using Nom.Orch.UtilityInterfaces;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// The re-check of links already made: withdrawing the waiting model picks, each rule set
    /// moving or taking back what the link gate refuses, the lane on the frame, and the undo by
    /// stamp that gives back the state before a run, byte for byte, except on recipes rebuilt since.
    /// </summary>
    public class LinkRecheckTests
    {
        private sealed class NoUser : ICurrentUserService
        {
            public long? PersonId => null;
            public long PersonIdOrSystem => SystemConstants.SystemPersonId;
            public long RequiredPersonId => throw new UnauthorizedAccessException();
            public string? UserId => null;
            public string RequiredUserId => throw new UnauthorizedAccessException();
        }

        private static ApplicationDbContext NewContext()
        {
            var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options);
            LinkRecheckClassBarTests.Qualify(db, LinkRecheckRules.AlcoholClass, LinkGate.Prepared);
            return db;
        }

        private static LinkRecheckService Recheck(ApplicationDbContext db) =>
            new(db, new NoUser(), new LinkAutoApplyService(db, new MemoryCache(new MemoryCacheOptions())), new LineRelinkService(db, new NoUser()),
                new LaneFrameService(db, Array.Empty<ILaneUndo>(), NullLogger<LaneFrameService>.Instance), NullLogger<LinkRecheckService>.Instance);

        private static IngredientEntity Food(string name, string? description = null, string? fdcId = null, int? category = null, long status = (long)CurationStatusEnum.Curated)
        {
            var food = new IngredientEntity
            {
                Name = name,
                CurationStatusId = status,
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

        private static FoodCatalogProposalEntity Pick(long? ingredientId, string wording, long? target, FoodProposalStatus status = FoodProposalStatus.Pending, string source = "ai:qwen2.5:14b-instruct") => new()
        {
            IngredientId = ingredientId, Field = FoodCatalogReviewService.FdcLinkField, Status = status, Batch = IngredientLinkService.Batch,
            Source = source, CurrentValue = wording, ProposedValue = target?.ToString(CultureInfo.InvariantCulture),
        };

        /// <summary>Every recipe line, ingredient, alias and link proposal, without timestamps.</summary>
        private static async Task<string> FingerprintAsync(ApplicationDbContext db)
        {
            db.ChangeTracker.Clear();
            var lines = await db.RecipeIngredients.IgnoreQueryFilters().AsNoTracking()
                .OrderBy(ri => ri.RecipeId).ThenBy(ri => ri.SortOrder).ThenBy(ri => ri.IngredientId)
                .Select(ri => $"line {ri.RecipeId}:{ri.SortOrder}:{ri.IngredientId}:{ri.RawLine}:{ri.IsDeleted}")
                .ToListAsync();
            var foods = await db.Ingredients.IgnoreQueryFilters().AsNoTracking().OrderBy(i => i.Id)
                .Select(i => $"food {i.Id}:{i.Name}:{i.CurationStatusId}:{i.IsDeleted}").ToListAsync();
            var aliases = await db.IngredientAliases.IgnoreQueryFilters().AsNoTracking().OrderBy(a => a.IngredientId).ThenBy(a => a.AliasName)
                .Select(a => $"alias {a.IngredientId}:{a.AliasName}:{a.SourceContext}:{a.IsDeleted}").ToListAsync();
            var proposals = await db.FoodCatalogProposals.IgnoreQueryFilters().AsNoTracking().OrderBy(p => p.Id)
                .Select(p => $"proposal {p.Id}:{p.IngredientId}:{p.Status}:{p.ProposedValue}:{p.IsDeleted}").ToListAsync();
            return string.Join("\n", lines.Concat(foods).Concat(aliases).Concat(proposals));
        }

        private static async Task<long> FoodOfAsync(ApplicationDbContext db, long recipeId, string rawLine)
        {
            db.ChangeTracker.Clear();
            return await db.RecipeIngredients.AsNoTracking().Where(ri => ri.RecipeId == recipeId && ri.RawLine == rawLine).Select(ri => ri.IngredientId).SingleAsync();
        }

        [Fact]
        public async Task Withdrawing_the_waiting_model_picks_journals_each_under_one_stamp_and_its_undo_puts_them_back()
        {
            await using var db = NewContext();
            var holder = Food("brandy", status: (long)CurationStatusEnum.NonCurated);
            var other = Food("medium jalapeno", status: (long)CurationStatusEnum.NonCurated);
            var asked = Food("halloumi", status: (long)CurationStatusEnum.NonCurated);
            var liqueur = Food("Coffee Cream Liqueur");
            db.Ingredients.AddRange(holder, other, asked, liqueur);
            await db.SaveChangesAsync();
            var waiting = Pick(holder.Id, "brandy", liqueur.Id);
            var second = Pick(other.Id, "medium jalapeno", liqueur.Id);
            var third = Pick(asked.Id, "halloumi", liqueur.Id);
            var reviewed = Pick(holder.Id, "brandy", liqueur.Id);
            reviewed.ReviewedByPersonId = 7;
            var nameMatch = Pick(other.Id, "medium jalapeno", liqueur.Id, source: IngredientLinkService.ExactSource);
            var applied = Pick(other.Id, "jalapeno", liqueur.Id, FoodProposalStatus.Applied);
            db.FoodCatalogProposals.AddRange(waiting, second, third, reviewed, nameMatch, applied);
            await db.SaveChangesAsync();
            var service = Recheck(db);
            var before = await FingerprintAsync(db);

            var dry = await service.WithdrawPicksAsync();
            (dry.Applied, dry.Examined, dry.Withdrawn, dry.Stamp).Should().Be((false, 3, 3, (DateTime?)null));
            (await FingerprintAsync(db)).Should().Be(before, "a dry run writes nothing");

            var run = await service.WithdrawPicksAsync(apply: true);
            run.Withdrawn.Should().Be(3);
            run.Stamp.Should().NotBeNull();
            db.ChangeTracker.Clear();
            (await db.FoodCatalogProposals.IgnoreQueryFilters().Where(p => p.IsDeleted).Select(p => p.Id).ToListAsync())
                .Should().BeEquivalentTo(new[] { waiting.Id, second.Id, third.Id }, "a reviewed pick, a name match and an applied link are not waiting model picks");
            (await db.AuditLogEntries.CountAsync(a => a.ChangeType == LinkRecheckService.WithdrawChangeType && a.Timestamp == run.Stamp)).Should().Be(3);
            (await db.AuditLogEntries.CountAsync(a => a.ChangeType == LineRelinkService.ProposalWithdrawChangeType)).Should().Be(0,
                "a withdrawal of its own type is never reopened by the time-window relink undo or a wording retract");

            var again = Pick(asked.Id, "halloumi", null, FoodProposalStatus.Rejected);
            again.CreatedDate = DateTime.UtcNow.AddSeconds(1);
            db.FoodCatalogProposals.Add(again);
            await db.SaveChangesAsync();
            var undo = await service.UndoAsync(run.Stamp!.Value, apply: true);
            undo.Found.Should().BeTrue();
            db.ChangeTracker.Clear();
            (await db.FoodCatalogProposals.IgnoreQueryFilters().Where(p => p.IsDeleted).Select(p => p.Id).ToListAsync())
                .Should().Equal(new[] { third.Id }, "the halloumi wording was asked again since, so its old pick stays withdrawn");
        }

        private sealed record Bar(IngredientEntity Vinegar, IngredientEntity Liqueur, IngredientEntity Juice, IngredientEntity DrySherry, IngredientEntity Spirit,
            RecipeEntity Trifle, RecipeEntity Flip, RecipeEntity Duck, RecipeEntity Salad);

        private static async Task<Bar> BarAsync(ApplicationDbContext db)
        {
            var vinegar = Food("Red Wine Vinegar", "Vinegar, red wine", "172241", 2);
            var liqueur = Food("Coffee Cream Liqueur", "Alcoholic beverage, liqueur, coffee with cream, 34 proof", "175098", 14);
            var juice = Food("Grape Juice", "Grape juice, canned or bottled, unsweetened", "174156", 9);
            var drySherry = Food("Dessert Wine, Dry", "Alcoholic beverage, wine, dessert, dry", "175112", 14);
            var spirit = Food("Distilled Spirit", "Alcoholic beverage, distilled, all (gin, rum, vodka, whiskey) 80 proof", "174815", 14);
            db.Ingredients.AddRange(vinegar, liqueur, juice, drySherry, spirit);
            await db.SaveChangesAsync();
            var trifle = Scraped("Trifle", (vinegar, "1 glass of sherry"));
            var flip = Scraped("Brandy flip", (liqueur, "4 tablespoonfuls of brandy"));
            var duck = Scraped("Duck with orange", (juice, "1 glass Grand Marnier"));
            var salad = Scraped("Salad", (vinegar, "2 tbsp sherry vinegar"));
            db.Recipes.AddRange(trifle, flip, duck, salad);
            await db.SaveChangesAsync();
            return new Bar(vinegar, liqueur, juice, drySherry, spirit, trifle, flip, duck, salad);
        }

        [Fact]
        public async Task The_alcohol_set_moves_a_drink_line_to_its_senses_food_takes_back_one_with_no_food_and_its_undo_gives_back_the_state_before()
        {
            await using var db = NewContext();
            var bar = await BarAsync(db);
            db.IngredientAliases.Add(new IngredientAliasEntity { IngredientId = bar.Liqueur.Id, AliasName = "brandy", SourceContext = "link:ai:qwen2.5:14b-instruct" });
            db.Ingredients.Add(new IngredientEntity { Name = "grand marnier", CurationStatusId = (long)CurationStatusEnum.NonCurated, IsDeleted = true });
            await db.SaveChangesAsync();
            var service = Recheck(db);
            var before = await FingerprintAsync(db);

            var dry = await service.RunAsync(LinkRecheckRules.Alcohol);
            dry!.Applied.Should().BeFalse();
            dry.Samples.Should().Contain(s => s.Outcome == LinkRecheckRules.Move && s.Wording == "sherry" && s.TargetId == bar.DrySherry.Id && s.Tier == LinkRecheckRules.AlcoholSenseTier);
            dry.Samples.Should().Contain(s => s.Outcome == LinkRecheckRules.Move && s.FoodId == bar.Liqueur.Id && s.TargetId == bar.Spirit.Id);
            dry.Samples.Should().Contain(s => s.Outcome == LinkRecheckRules.Unlink && s.FoodId == bar.Juice.Id);
            dry.Samples.Should().NotContain(s => s.ExampleLines.Contains("2 tbsp sherry vinegar"), "a vinegar line on a vinegar is right");
            (await FingerprintAsync(db)).Should().Be(before);

            var run = await service.RunAsync(LinkRecheckRules.Alcohol, apply: true);
            (run!.LinesMoved, run.LinesUnlinked, run.FoodsCreated).Should().Be((2, 1, 1));
            (await FoodOfAsync(db, bar.Trifle.Id, "1 glass of sherry")).Should().Be(bar.DrySherry.Id);
            (await FoodOfAsync(db, bar.Flip.Id, "4 tablespoonfuls of brandy")).Should().Be(bar.Spirit.Id);
            (await FoodOfAsync(db, bar.Salad.Id, "2 tbsp sherry vinegar")).Should().Be(bar.Vinegar.Id);
            var home = await FoodOfAsync(db, bar.Duck.Id, "1 glass Grand Marnier");
            (await db.Ingredients.SingleAsync(i => i.Id == home)).Should().Match<IngredientEntity>(i => i.CurationStatusId == (long)CurationStatusEnum.NonCurated && i.FdcId == null);
            (await db.IngredientAliases.IgnoreQueryFilters().SingleAsync(a => a.IngredientId == bar.Liqueur.Id && a.AliasName == "brandy")).IsDeleted.Should().BeTrue("the model pick's alias stops teaching the wrong food");
            (await db.IngredientAliases.SingleAsync(a => a.IngredientId == bar.Spirit.Id && a.SourceContext!.StartsWith(LinkRecheckService.MovedAliasPrefix))).AliasName.Should().NotBeEmpty();
            (await db.AuditLogEntries.CountAsync(a => a.ChangeType == LinkRecheckService.LineChangeType && a.Timestamp == run.Stamp)).Should().Be(3);

            var undoDry = await service.UndoAsync(run.Stamp!.Value);
            (undoDry.Found, undoDry.LineEntries, undoDry.Applied).Should().Be((true, 3, false));
            var undo = await service.UndoAsync(run.Stamp.Value, apply: true);
            (undo.LinesRestored, undo.FoodsRemoved).Should().Be((3, 1));
            (await FingerprintAsync(db)).Should().Be(before, "the undo restores lines, aliases, the freed name and removes the food it created");
            (await service.UndoAsync(run.Stamp.Value, apply: true)).LinesRestored.Should().Be(0, "a second undo finds nothing left");
        }

        [Fact]
        public async Task The_alcohol_set_removes_a_model_alias_that_crosses_senses_even_with_no_line_and_creates_no_food_for_it()
        {
            await using var db = NewContext();
            var vinegar = Food("Apple Cider Vinegar", "Vinegar, cider", "173469", 2);
            db.Ingredients.Add(vinegar);
            await db.SaveChangesAsync();
            db.IngredientAliases.AddRange(
                new IngredientAliasEntity { IngredientId = vinegar.Id, AliasName = "sweet cider", SourceContext = null },
                new IngredientAliasEntity { IngredientId = vinegar.Id, AliasName = "cider vinegar", SourceContext = null });
            await db.SaveChangesAsync();
            var service = Recheck(db);
            var before = await FingerprintAsync(db);

            var run = await service.RunAsync(LinkRecheckRules.Alcohol, apply: true);
            (run!.FoodsCreated, run.AliasesRemoved).Should().Be((0, 1));
            db.ChangeTracker.Clear();
            (await db.IngredientAliases.IgnoreQueryFilters().SingleAsync(a => a.AliasName == "sweet cider")).IsDeleted.Should().BeTrue("sweet cider is apple juice, not a vinegar");
            (await db.IngredientAliases.SingleAsync(a => a.AliasName == "cider vinegar")).IsDeleted.Should().BeFalse();
            await service.UndoAsync(run.Stamp!.Value, apply: true);
            (await FingerprintAsync(db)).Should().Be(before);
        }

        [Fact]
        public async Task An_undo_leaves_the_lines_of_a_recipe_rebuilt_since_and_says_so()
        {
            await using var db = NewContext();
            var bar = await BarAsync(db);
            var service = Recheck(db);
            var run = await service.RunAsync(LinkRecheckRules.Alcohol, apply: true);
            db.AuditLogEntries.Add(new AuditLogEntryEntity
            {
                EntityType = ScrapedRecipeRepairService.LineEntityType, EntityId = bar.Trifle.Id, ChangeType = RecipeReprocessService.ReprocessChangeType,
                Timestamp = run!.Stamp!.Value.AddMinutes(1), ChangedByPersonId = SystemConstants.SystemPersonId,
            });
            await db.SaveChangesAsync();

            var dry = await service.UndoAsync(run.Stamp.Value);
            dry.OnRebuiltRecipes.Should().Be(1);
            var undo = await service.UndoAsync(run.Stamp.Value, apply: true);
            undo.SkippedRebuilt.Should().Be(1);
            (await FoodOfAsync(db, bar.Trifle.Id, "1 glass of sherry")).Should().Be(bar.DrySherry.Id, "the rebuild replaced the recipe's lines after the move");
            (await FoodOfAsync(db, bar.Flip.Id, "4 tablespoonfuls of brandy")).Should().Be(bar.Liqueur.Id);
        }

        [Fact]
        public async Task The_part_and_herb_set_moves_whole_potatoes_off_the_skins_and_takes_a_fresh_herb_off_the_dried_spice()
        {
            using var none = LinkRecheckRules.AssumeProven();
            await using var db = NewContext();
            var skins = Food("Raw Potato Skins", "Potatoes, raw, skin", "170032", 11);
            var potatoes = Food("Potatoes", "Potatoes, flesh and skin, raw", "170026", 11);
            var tarragon = Food("Dried Tarragon", "Spices, tarragon, dried", "170937", 2);
            db.Ingredients.AddRange(skins, potatoes, tarragon);
            await db.SaveChangesAsync();
            db.IngredientAliases.AddRange(
                new IngredientAliasEntity { IngredientId = skins.Id, AliasName = "large potatoes", SourceContext = IngredientAliasTrust.RecheckPrefix + "name-match" },
                new IngredientAliasEntity { IngredientId = tarragon.Id, AliasName = "freshly chopped tarragon", SourceContext = IngredientAliasTrust.RecheckPrefix + "name-match" });
            var soup = Scraped("Potato soup", (skins, "4 large potatoes"), (tarragon, "2 tbsp freshly chopped tarragon"));
            db.Recipes.Add(soup);
            await db.SaveChangesAsync();
            var service = Recheck(db);
            var before = await FingerprintAsync(db);

            var kept = await service.RunAsync(LinkRecheckRules.PartHerb);
            (kept!.LinesMoved, kept.LinesUnlinked, kept.Candidates, kept.CandidateLines).Should().Be((0, 0, 2, 2),
                "part and fresh herb are classes not proven: an operator's run keeps their lines and counts them as candidates for the model audit");
            kept.ByCandidate.Keys.Should().BeEquivalentTo(LinkGate.Part, LinkGate.FreshHerb);

            LinkRecheckClassBarTests.Qualify(db, LinkGate.Part, LinkGate.FreshHerb);
            var run = await service.RunLaneBatchAsync(LinkRecheckRules.PartHerb, LaneMode.Live, 50000);
            (await FoodOfAsync(db, soup.Id, "4 large potatoes")).Should().NotBe(skins.Id, "whole potatoes are not the skins; where the gate lets the potato entry through they go there, else back to their wording");
            var herb = await FoodOfAsync(db, soup.Id, "2 tbsp freshly chopped tarragon");
            herb.Should().NotBe(tarragon.Id, "a fresh herb is not the dried spice, and no fresh tarragon food exists");
            (await db.Ingredients.SingleAsync(i => i.Id == herb)).CurationStatusId.Should().Be((long)CurationStatusEnum.NonCurated);
            (await db.IngredientAliases.IgnoreQueryFilters().SingleAsync(a => a.IngredientId == skins.Id && a.AliasName == "large potatoes")).IsDeleted.Should().BeTrue();
            run.ByReason.Keys.Should().Contain(new[] { "food is a part the wording does not name", "a fresh herb is not the dried spice" }, "a lane batch writes the candidates of classes whose own tier the link-audit record qualified");
            run.ClassStamps.Should().HaveCount(2, "each class writes under a stamp of its own");

            foreach (var stamp in run.ClassStamps) await service.UndoAsync(stamp, apply: true);
            (await FingerprintAsync(db)).Should().Be(before);
        }

        [Fact]
        public async Task The_compound_set_takes_an_applied_model_link_of_two_foods_back_and_leaves_a_line_the_compound_lane_put_on_an_alternative()
        {
            await using var db = NewContext();
            var salt = Food("Salt");
            var butter = Food("Butter");
            var pepper = Food("Black Pepper", "Spices, pepper, black", "170931", 2);
            db.Ingredients.AddRange(salt, butter, pepper);
            await db.SaveChangesAsync();
            db.FoodCatalogProposals.AddRange(
                Pick(null, "salt and pepper", salt.Id, FoodProposalStatus.Applied),
                Pick(null, "butter or margarine", butter.Id, FoodProposalStatus.Applied));
            var stew = Scraped("Stew", (salt, "salt and pepper"), (butter, "butter or margarine"));
            db.Recipes.Add(stew);
            await db.SaveChangesAsync();
            db.AuditLogEntries.Add(new AuditLogEntryEntity
            {
                EntityType = LineRelinkService.RelinkEntityType, EntityId = stew.Id, ChangeType = CompoundLineService.AlternativeChangeType,
                OldValue = LineRelinkService.Place(999, 1), NewValue = LineRelinkService.Place(butter.Id, 1), ChangedByPersonId = SystemConstants.SystemPersonId,
            });
            await db.SaveChangesAsync();

            (await Recheck(db).RunAsync(LinkRecheckRules.Compound))!.Candidates.Should().Be(1, "the compound class is not proven");
            LinkRecheckClassBarTests.Qualify(db, LinkGate.Compound);
            var run = await Recheck(db).RunLaneBatchAsync(LinkRecheckRules.Compound, LaneMode.Live, 50000);
            run.LinesUnlinked.Should().Be(1);
            run.Alternatives.Should().Be(1);
            var home = await FoodOfAsync(db, stew.Id, "salt and pepper");
            (await db.Ingredients.SingleAsync(i => i.Id == home)).Name.Should().Be("salt and pepper");
            (await FoodOfAsync(db, stew.Id, "butter or margarine")).Should().Be(butter.Id);
        }

        [Fact]
        public async Task The_container_set_moves_a_can_line_to_the_one_canned_sister_and_keeps_one_with_two()
        {
            await using var db = NewContext();
            var tomatoes = Food("Tomatoes", "Tomatoes, red, ripe, raw, year round average", "170457", 11);
            var canned = Food("Canned Tomatoes", "Tomatoes, red, ripe, canned, packed in tomato juice", "170051", 11);
            var beans = Food("Black Beans", "Beans, black, mature seeds, raw", "173734", 16);
            var cannedBeans = Food("Canned Black Beans", "Beans, black, mature seeds, canned, low sodium", "175188", 16);
            var drainedBeans = Food("Drained Black Beans", "Beans, black, mature seeds, canned, drained", "175189", 16);
            var otherCanned = Food("Canned Black Beans Salted", "Beans, black, mature seeds, canned, with salt", "175190", 16);
            db.Ingredients.AddRange(tomatoes, canned, beans, cannedBeans, drainedBeans, otherCanned);
            await db.SaveChangesAsync();
            var chili = Scraped("Chili", (tomatoes, "1 can tomatoes"), (beans, "1 (15 oz) can black beans"), (tomatoes, "2 tomatoes, you can use any"));
            db.Recipes.Add(chili);
            await db.SaveChangesAsync();

            var operatorRun = await Recheck(db).RunAsync(LinkRecheckRules.ContainerForm, apply: true);
            (operatorRun!.LinesMoved, operatorRun.Candidates).Should().Be((0, 1), "the container class is not proven: its move is a candidate");
            (await FoodOfAsync(db, chili.Id, "1 can tomatoes")).Should().Be(tomatoes.Id);
            db.LaneDecisions.RemoveRange(db.LaneDecisions);
            await db.SaveChangesAsync();
            LinkRecheckClassBarTests.Qualify(db, LinkRecheckRules.ContainerClass);

            var run = await Recheck(db).RunLaneBatchAsync(LinkRecheckRules.ContainerForm, LaneMode.Live, 50000);
            (await FoodOfAsync(db, chili.Id, "1 can tomatoes")).Should().Be(canned.Id);
            (await FoodOfAsync(db, chili.Id, "1 (15 oz) can black beans")).Should().Be(beans.Id, "two canned black beans that are not drained: no one sister, so the line stays");
            (await FoodOfAsync(db, chili.Id, "2 tomatoes, you can use any")).Should().Be(tomatoes.Id, "'you can' is not a can");
            (run.LinesMoved, run.LinesUnlinked, run.Held).Should().Be((1, 0, 1));
        }

        [Fact]
        public async Task The_prepared_set_takes_every_line_off_a_prepared_food()
        {
            await using var db = NewContext();
            var tortellini = Food("Cheese Tortellini", "Restaurant, Italian, cheese tortellini", "171234", 25);
            db.Ingredients.Add(tortellini);
            await db.SaveChangesAsync();
            var dinner = Scraped("Dinner", (tortellini, "1 lb cheese tortellini"));
            db.Recipes.Add(dinner);
            await db.SaveChangesAsync();

            var run = await Recheck(db).RunAsync(LinkRecheckRules.Prepared, apply: true);
            run!.LinesUnlinked.Should().Be(1);
            var home = await FoodOfAsync(db, dinner.Id, "1 lb cheese tortellini");
            home.Should().NotBe(tortellini.Id);
            (await db.Ingredients.SingleAsync(i => i.Id == home)).Name.Should().Be("cheese tortellini");
        }

        [Fact]
        public async Task The_free_of_set_takes_back_a_line_marked_free_of_what_its_food_holds_to_a_food_of_unknown_contents_where_the_diet_guard_would_lower_a_flag_and_leaves_a_stand_in_and_a_food_named_first()
        {
            await using var db = NewContext();
            LinkRecheckClassBarTests.Qualify(db, LinkRecheckRules.FreeOf);
            var sauce = Food("Worcestershire Sauce", "Sauce, worcestershire", "171609", 6);
            var cheese = Food("Cream Cheese", "Cream cheese, full fat, block", "173418", 1);
            var imitation = Food("Imitation Sour Cream", "Sour cream, imitation, cultured", "171257", 1);
            var butter = Food("Butter", "Butter, salted", "173410", 1);
            db.Ingredients.AddRange(sauce, cheese, imitation, butter);
            await db.SaveChangesAsync();
            var dip = Scraped("Dip", (sauce, "1 Tbsp vegan worcestershire sauce"), (cheese, "8 ounces vegan cream cheese"), (imitation, "1 cup vegan sour cream"), (butter, "1/2 cup butter or vegan butter"));
            db.Recipes.Add(dip);
            await db.SaveChangesAsync();

            var run = await Recheck(db).RunAsync(LinkRecheckRules.FreeOf, apply: true);

            run!.Groups.Should().Be(2, "the sauce and the cream cheese lines are marked free of what their foods hold; the stand-in and the food named first are not read");
            run.LinesUnlinked.Should().Be(2);
            (run.Held, run.UnknownGroups).Should().Be((0, 1), "the cream cheese line's own words do not say it lacks milk for every type its take-back would lower, so it goes to a food of unknown contents, not back to the cream cheese (staging checkpoint 7, S15)");
            var home = await FoodOfAsync(db, dip.Id, "1 Tbsp vegan worcestershire sauce");
            home.Should().NotBe(sauce.Id);
            (await db.Ingredients.SingleAsync(i => i.Id == home)).Name.Should().Be("vegan worcestershire sauce");
            (await db.IngredientAliases.AnyAsync(a => a.IngredientId == home && a.SourceContext == RecheckDietGuard.UnknownContentsContext)).Should().BeFalse();
            var unknown = await FoodOfAsync(db, dip.Id, "8 ounces vegan cream cheese");
            unknown.Should().NotBe(cheese.Id);
            (await db.IngredientAliases.AnyAsync(a => a.IngredientId == unknown && a.SourceContext == RecheckDietGuard.UnknownContentsContext)).Should().BeTrue();
            (await FoodOfAsync(db, dip.Id, "1 cup vegan sour cream")).Should().Be(imitation.Id);
            (await FoodOfAsync(db, dip.Id, "1/2 cup butter or vegan butter")).Should().Be(butter.Id);
        }

        [Fact]
        public async Task The_applied_sets_split_the_november_third_links_from_the_rest_and_move_a_wording_to_the_curated_food_of_its_name()
        {
            await using var db = NewContext();
            var canola = Food("Canola Oil");
            var olive = Food("Olive Oil");
            var flour = Food("Flour");
            db.Ingredients.AddRange(canola, olive, flour);
            await db.SaveChangesAsync();
            var november = Pick(null, "olive oil", canola.Id, FoodProposalStatus.Applied);
            november.ReviewedAt = LinkRecheckRules.NovemberThird.AddHours(22);
            var october = Pick(null, "flour", flour.Id, FoodProposalStatus.Applied);
            october.ReviewedAt = new DateTime(2026, 10, 12, 0, 0, 0, DateTimeKind.Utc);
            db.FoodCatalogProposals.AddRange(november, october);
            var cake = Scraped("Cake", (canola, "2 tbsp olive oil"), (flour, "2 cups flour"));
            db.Recipes.Add(cake);
            await db.SaveChangesAsync();
            var service = Recheck(db);

            (await service.RunAsync(LinkRecheckRules.Applied))!.Groups.Should().Be(1, "only the October link");
            (await service.RunAsync(LinkRecheckRules.AppliedNovember))!.Candidates.Should().Be(1, "another curated food's name is a class not proven");
            LinkRecheckClassBarTests.Qualify(db, LinkGate.OtherFood);
            var run = await service.RunLaneBatchAsync(LinkRecheckRules.AppliedNovember, LaneMode.Live, 50000);
            run.Groups.Should().Be(1);
            run.ByTier.Should().ContainKey(LinkRecheckRules.CuratedNameTier);
            (await FoodOfAsync(db, cake.Id, "2 tbsp olive oil")).Should().Be(olive.Id);
            (await FoodOfAsync(db, cake.Id, "2 cups flour")).Should().Be(flour.Id);
        }

        [Fact]
        public async Task A_wording_the_verified_table_or_a_person_gives_to_its_food_is_left_and_listed()
        {
            await using var db = NewContext();
            var bar = await BarAsync(db);
            db.IngredientWordings.Add(new IngredientWordingEntity { Wording = "sherry", Verdict = IngredientWordingVerdict.Food, IngredientId = bar.Vinegar.Id, Batch = "w01", Source = "test" });
            db.IngredientAliases.Add(new IngredientAliasEntity { IngredientId = bar.Liqueur.Id, AliasName = "brandy", SourceContext = IngredientAliasTrust.AdminPrefix + "7" });
            await db.SaveChangesAsync();

            var run = await Recheck(db).RunAsync(LinkRecheckRules.Alcohol, apply: true);
            run!.Verified.Should().Be(2);
            (await FoodOfAsync(db, bar.Trifle.Id, "1 glass of sherry")).Should().Be(bar.Vinegar.Id, "the wording row is taken back by its retract, which keeps the rejection");
            (await FoodOfAsync(db, bar.Flip.Id, "4 tablespoonfuls of brandy")).Should().Be(bar.Liqueur.Id);

            var rows = await Recheck(db).RunAsync(LinkRecheckRules.Wordings, apply: true);
            rows!.Applied.Should().BeFalse("the wordings set only reports");
            rows.Samples.Should().Contain(s => s.WordingRowId != null && s.Outcome == LinkRecheckRules.Move);
        }

        [Fact]
        public async Task The_lane_decides_in_shadow_without_writing_skips_what_it_decided_and_writes_and_undoes_its_own_runs_when_live()
        {
            using var none = LinkRecheckRules.AssumeProven();
            await using var db = NewContext();
            var bar = await BarAsync(db);
            db.LaneStates.RemoveRange(db.LaneStates);
            await db.SaveChangesAsync();
            var service = Recheck(db);
            var before = await FingerprintAsync(db);
            var tier = LinkRecheckRules.CandidateTier(LinkRecheckRules.AlcoholClass);

            var shadow = await service.RunLaneBatchAsync(LinkRecheckRules.Alcohol, LaneMode.Live, 100);
            (shadow.LinesMoved + shadow.LinesUnlinked, shadow.ClassStamps.Count, shadow.Groups).Should().Be((0, 0, 3), "the set is live, but the alcohol class's own tier is in shadow");
            (await FingerprintAsync(db)).Should().Be(before);
            var decisions = await db.LaneDecisions.AsNoTracking().Where(d => d.Lane == AiLanes.LinkRecheck && d.Tier == tier).ToListAsync();
            decisions.Should().HaveCount(3).And.OnlyContain(d => d.Mode == LaneMode.Shadow && !d.Written && d.Writes && d.Stratum == LinkRecheckRules.Alcohol && d.SignatureHash == LinkRecheckRules.TierSignature(tier));
            (await service.RunLaneBatchAsync(LinkRecheckRules.Alcohol, LaneMode.Shadow, 100)).Groups.Should().Be(0, "what it decided under this signature it does not decide again");
            (await service.DecidedTodayAsync(LinkRecheckRules.Alcohol)).Should().Be(3);

            db.LaneDecisions.RemoveRange(db.LaneDecisions);
            await db.SaveChangesAsync();
            LinkRecheckClassBarTests.Qualify(db, LinkRecheckRules.AlcoholClass);
            var live = await service.RunLaneBatchAsync(LinkRecheckRules.Alcohol, LaneMode.Shadow, 100);
            live.Applied.Should().BeTrue("the class's tier is live, whatever the set's mode");
            (await FoodOfAsync(db, bar.Trifle.Id, "1 glass of sherry")).Should().Be(bar.DrySherry.Id);
            (await db.LaneDecisions.CountAsync(d => d.Written && d.Tier == tier)).Should().Be(3);
            var stamp = live.ClassStamps.Should().ContainSingle().Subject;

            var undo = new LinkRecheckLaneUndo(service, db);
            (await undo.UndoSinceAsync(LinkRecheckRules.Alcohol, stamp.AddSeconds(-1), apply: false, CancellationToken.None)).Undone.Should().Be(0, "the set's tier wrote nothing");
            (await undo.UndoSinceAsync(tier, stamp.AddSeconds(-1), apply: false, CancellationToken.None)).Undone.Should().Be(5, "three lines and the two trusted aliases on the foods they moved to");
            (await undo.UndoSinceAsync(tier, stamp.AddSeconds(-1), apply: true, CancellationToken.None)).Undone.Should().Be(5);
            (await FingerprintAsync(db)).Should().Be(before);
            (await undo.UndoSinceAsync(tier, stamp.AddSeconds(-1), apply: true, CancellationToken.None)).Undone.Should().Be(0);
        }

        [Fact]
        public async Task The_lane_records_a_wording_it_leaves_so_a_verified_row_does_not_hold_the_head_of_every_batch()
        {
            await using var db = NewContext();
            var bar = await BarAsync(db);
            db.IngredientWordings.Add(new IngredientWordingEntity { Wording = "sherry", Verdict = IngredientWordingVerdict.Food, IngredientId = bar.Vinegar.Id, Batch = "w01", Source = "test" });
            await db.SaveChangesAsync();
            var service = Recheck(db);

            var first = await service.RunLaneBatchAsync(LinkRecheckRules.Alcohol, LaneMode.Shadow, 1);
            (first.Groups, first.Verified).Should().Be((1, 1), "the sherry wording has the most lines and the verified row leaves it");
            var second = await service.RunLaneBatchAsync(LinkRecheckRules.Alcohol, LaneMode.Shadow, 1);
            (second.Groups, second.Verified).Should().Be((1, 0));
            (await db.LaneDecisions.CountAsync(d => !d.Writes && d.Decision!.StartsWith(LinkRecheckRules.Verified))).Should().Be(1);
        }

        [Fact]
        public async Task An_operator_run_leaves_the_lanes_undo_alone()
        {
            await using var db = NewContext();
            await BarAsync(db);
            var service = Recheck(db);
            var run = await service.RunAsync(LinkRecheckRules.Alcohol, apply: true);
            (await new LinkRecheckLaneUndo(service, db).UndoSinceAsync(LinkRecheckRules.Alcohol, run!.Stamp!.Value.AddSeconds(-1), true, CancellationToken.None)).Undone.Should().Be(0);
            (await db.LaneDecisions.CountAsync(d => d.Written && d.Mode == LaneMode.Live)).Should().Be(3, "an operator's apply is recorded for the auditor sample");
        }

        [Fact]
        public async Task The_name_check_lists_a_name_its_entry_contradicts_renames_the_mustard_and_the_undo_gives_the_name_back()
        {
            await using var db = NewContext();
            var mustard = Food("Black Mustard Seed", "Spices, mustard seed, ground", "170929", 2);
            var squid = Food("Fresh Squid", "Mollusks, squid, mixed species, raw", "174215", 15);
            db.Ingredients.AddRange(mustard, squid);
            await db.SaveChangesAsync();
            var service = Recheck(db);

            var dry = await service.CheckNamesAsync();
            dry.Listed.Should().ContainSingle().Which.Should().Match<LinkRecheckNameRow>(r => r.IngredientId == mustard.Id && r.NewName == "Ground Mustard");
            var run = await service.CheckNamesAsync(apply: true);
            run.Renamed.Should().Be(1);
            db.ChangeTracker.Clear();
            (await db.Ingredients.SingleAsync(i => i.Id == mustard.Id)).Name.Should().Be("Ground Mustard");
            (await service.UndoAsync(run.Stamp!.Value, apply: true)).NamesRestored.Should().Be(1);
            db.ChangeTracker.Clear();
            (await db.Ingredients.SingleAsync(i => i.Id == mustard.Id)).Name.Should().Be("Black Mustard Seed");
        }

        [Fact]
        public async Task An_unknown_set_is_refused_and_the_status_shows_the_newest_runs()
        {
            await using var db = NewContext();
            await BarAsync(db);
            var service = Recheck(db);
            (await service.RunAsync("everything")).Should().BeNull();
            var run = await service.RunAsync(LinkRecheckRules.Alcohol, apply: true);
            await service.UndoAsync(run!.Stamp!.Value, apply: true);
            var status = await service.StatusAsync(enabled: false);
            status.LastRuns.Should().ContainSingle(r => r.Set == LinkRecheckRules.Alcohol).Which.Should().Match<LinkRecheckRunSummary>(r => r.Undone && r.LinesMoved == 2 && r.LinesUnlinked == 1);
            status.Sets.Should().Equal(LinkRecheckRules.AllSets);
        }

        private sealed class FakeJudge : IIngredientLinkVerifier
        {
            private readonly LinkJudgement _answer;
            public FakeJudge(LinkJudgement answer, string model = "phi4") => (_answer, ModelName) = (answer, model);
            public bool IsConfigured => true;
            public string ModelName { get; }
            public int PromptVersion => 2;
            public List<LinkPair> Asked { get; } = new();

            public Task<IReadOnlyList<LinkVerdict>> JudgeAsync(IReadOnlyList<LinkPair> pairs, CancellationToken cancellationToken = default)
            {
                Asked.AddRange(pairs);
                return Task.FromResult<IReadOnlyList<LinkVerdict>>(pairs.Select(p => new LinkVerdict(p.IngredientId, _answer)).ToList());
            }
        }

        private static LaneDecisionEntity Decision(string outcome) => new()
        {
            Lane = AiLanes.LinkRecheck, Tier = LinkRecheckRules.Alcohol, Subject = "1:sherry", Mode = LaneMode.Live,
            SignalsJson = LaneJournal.Write(new LinkRecheckService.LinkRecheckSignals(LinkRecheckRules.Alcohol, 1, "Red Wine Vinegar", "Vinegar, red wine", "sherry", "fortified-dry", 1,
                new[] { "1 glass of sherry" }, new[] { "a drink line on a food outside its sense" }, outcome, null, LinkRecheckRules.AlcoholSenseTier, 2, "Dessert Wine, Dry",
                "Alcoholic beverage, wine, dessert, dry", null)),
        };

        [Theory]
        [InlineData(LinkRecheckRules.Move, LinkJudgement.Same, false)]
        [InlineData(LinkRecheckRules.Move, LinkJudgement.DifferentFood, true)]
        [InlineData(LinkRecheckRules.Move, LinkJudgement.VarietyOrForm, true)]
        [InlineData(LinkRecheckRules.Unlink, LinkJudgement.Same, true)]
        [InlineData(LinkRecheckRules.Unlink, LinkJudgement.DifferentFood, false)]
        [InlineData(LinkRecheckRules.Move, LinkJudgement.Unsure, null)]
        public async Task The_auditor_finds_a_move_wrong_unless_the_line_is_the_new_food_and_a_take_back_wrong_when_it_was_the_old_one(string outcome, LinkJudgement answer, bool? wrong)
        {
            var judge = new FakeJudge(answer);
            var audit = new LinkRecheckAudit(judge);
            (await audit.AuditAsync(Decision(outcome), CancellationToken.None)).Wrong.Should().Be(wrong);
            (await audit.ConfirmsWrongAsync(Decision(outcome), CancellationToken.None)).Should().Be(wrong);
            judge.Asked.Should().OnlyContain(p => p.Wording == "1 glass of sherry"
                && p.Description == (outcome == LinkRecheckRules.Move ? "Alcoholic beverage, wine, dessert, dry" : "Vinegar, red wine"));
        }

        [Fact]
        public void The_auditor_and_its_confirming_reader_must_be_of_two_families()
        {
            LinkRecheckAudit.Problem(new FakeJudge(LinkJudgement.Same, "phi4"), new FakeJudge(LinkJudgement.Same, "mistral-nemo")).Should().BeNull();
            LinkRecheckAudit.Problem(new FakeJudge(LinkJudgement.Same, "phi4"), new FakeJudge(LinkJudgement.Same, "phi4-mini")).Should().Contain("phi");
        }

        /// <summary>Lets a number of saves that touch what <c>counts</c> names through, then fails the next one once, as a request cut off mid-run.</summary>
        private sealed class CutOff : SaveChangesInterceptor
        {
            private readonly Func<DbContext, bool> _counts;
            private int _left;

            public CutOff(int after, Func<DbContext, bool> counts) => (_left, _counts) = (after, counts);

            public bool Armed { get; set; }

            public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
            {
                if (Armed && eventData.Context != null && _counts(eventData.Context) && _left-- <= 0)
                {
                    Armed = false;
                    throw new OperationCanceledException("cut off");
                }
                return base.SavingChangesAsync(eventData, result, cancellationToken);
            }
        }

        private static ApplicationDbContext NewContext(CutOff cutOff) =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .AddInterceptors(cutOff)
                .Options);

        [Fact]
        public async Task A_withdrawal_cut_off_after_its_first_chunk_shows_its_stamp_and_is_undone_by_it()
        {
            var cutOff = new CutOff(1, ctx => ctx.ChangeTracker.Entries<FoodCatalogProposalEntity>().Any(e => e.State == EntityState.Modified));
            await using var db = NewContext(cutOff);
            var holder = Food("brandy", status: (long)CurationStatusEnum.NonCurated);
            var liqueur = Food("Coffee Cream Liqueur");
            db.Ingredients.AddRange(holder, liqueur);
            await db.SaveChangesAsync();
            db.FoodCatalogProposals.AddRange(Enumerable.Range(0, 1001).Select(_ => Pick(holder.Id, "brandy", liqueur.Id)));
            await db.SaveChangesAsync();
            var service = Recheck(db);

            cutOff.Armed = true;
            await FluentActions.Awaiting(() => service.WithdrawPicksAsync(apply: true)).Should().ThrowAsync<OperationCanceledException>();
            db.ChangeTracker.Clear();
            (await db.FoodCatalogProposals.IgnoreQueryFilters().CountAsync(p => p.IsDeleted)).Should().Be(1000, "the first chunk was committed");

            var status = await service.StatusAsync(enabled: false);
            status.LastWithdraw.Should().NotBeNull("the run's header is written before its first chunk, so the operator finds the stamp");
            status.LastWithdraw!.Complete.Should().BeFalse();
            var undo = await service.UndoAsync(status.LastWithdraw.Stamp, apply: true);
            undo.Found.Should().BeTrue();
            db.ChangeTracker.Clear();
            (await db.FoodCatalogProposals.IgnoreQueryFilters().CountAsync(p => p.IsDeleted)).Should().Be(0);
        }

        /// <summary>Once armed, runs <c>peek</c> inside the first save that touches what <c>counts</c> names, as another request reading the status while a run is going.</summary>
        private sealed class Peek : SaveChangesInterceptor
        {
            private readonly Func<DbContext, bool> _counts;
            private readonly Func<Task> _peek;

            public Peek(Func<DbContext, bool> counts, Func<Task> peek) => (_counts, _peek) = (counts, peek);

            public bool Armed { get; set; }

            public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
            {
                if (Armed && eventData.Context != null && _counts(eventData.Context))
                {
                    Armed = false;
                    await _peek();
                }
                return await base.SavingChangesAsync(eventData, result, cancellationToken);
            }
        }

        private static ApplicationDbContext SharedContext(InMemoryDatabaseRoot root, string name, params IInterceptor[] interceptors) =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(name, root)
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .AddInterceptors(interceptors)
                .Options);

        [Fact]
        public async Task A_withdrawal_still_going_shows_as_running_not_as_cut_off()
        {
            var root = new InMemoryDatabaseRoot();
            var name = Guid.NewGuid().ToString();
            await using var reader = SharedContext(root, name);
            LinkRecheckStatusModel? during = null;
            var peek = new Peek(ctx => ctx.ChangeTracker.Entries<FoodCatalogProposalEntity>().Any(e => e.State == EntityState.Modified),
                async () => during = await Recheck(reader).StatusAsync(enabled: false));
            await using var db = SharedContext(root, name, peek);
            var holder = Food("brandy", status: (long)CurationStatusEnum.NonCurated);
            var liqueur = Food("Coffee Cream Liqueur");
            db.Ingredients.AddRange(holder, liqueur);
            await db.SaveChangesAsync();
            db.FoodCatalogProposals.AddRange(Enumerable.Range(0, 3).Select(_ => Pick(holder.Id, "brandy", liqueur.Id)));
            await db.SaveChangesAsync();

            peek.Armed = true;
            await Recheck(db).WithdrawPicksAsync(apply: true);

            during!.LastWithdraw.Should().Match<LinkRecheckRunSummary>(w => !w.Complete && w.Running, "the withdrawal had written its header and not yet finished");
            reader.ChangeTracker.Clear();
            (await Recheck(reader).StatusAsync(enabled: false)).LastWithdraw.Should().Match<LinkRecheckRunSummary>(w => w.Complete && !w.Running);
        }

        [Fact]
        public async Task A_set_run_still_going_shows_as_running_and_once_cut_off_no_longer_does()
        {
            using var none = LinkRecheckRules.AssumeProven();
            var root = new InMemoryDatabaseRoot();
            var name = Guid.NewGuid().ToString();
            await using var reader = SharedContext(root, name);
            LinkRecheckStatusModel? during = null;
            var peek = new Peek(ctx => ctx.ChangeTracker.Entries<RecipeIngredientEntity>().Any(e => e.State is EntityState.Added or EntityState.Deleted),
                async () => during = await Recheck(reader).StatusAsync(enabled: false));
            var cutOff = new CutOff(1, ctx => ctx.ChangeTracker.Entries<RecipeIngredientEntity>().Any(e => e.State is EntityState.Added or EntityState.Deleted));
            await using var db = SharedContext(root, name, peek, cutOff);
            await BarAsync(db);
            LinkRecheckClassBarTests.Qualify(db, LinkRecheckRules.AlcoholClass);
            var tier = LinkRecheckRules.CandidateTier(LinkRecheckRules.AlcoholClass);

            peek.Armed = cutOff.Armed = true;
            await FluentActions.Awaiting(() => Recheck(db).RunLaneBatchAsync(LinkRecheckRules.Alcohol, LaneMode.Live, 100)).Should().ThrowAsync<OperationCanceledException>();

            during!.LastRuns.Should().ContainSingle(r => r.Set == tier).Which.Should().Match<LinkRecheckRunSummary>(r => !r.Complete && r.Running);
            reader.ChangeTracker.Clear();
            (await Recheck(reader).StatusAsync(enabled: false)).LastRuns.Should().ContainSingle(r => r.Set == tier)
                .Which.Should().Match<LinkRecheckRunSummary>(r => !r.Complete && !r.Running, "a run that threw is cut off, not running");
        }

        [Fact]
        public async Task A_set_run_cut_off_partway_shows_its_stamp_and_is_undone_by_it_and_by_the_lanes_undo()
        {
            using var none = LinkRecheckRules.AssumeProven();
            var cutOff = new CutOff(2, ctx => ctx.ChangeTracker.Entries<RecipeIngredientEntity>().Any(e => e.State is EntityState.Added or EntityState.Deleted));
            await using var db = NewContext(cutOff);
            await BarAsync(db);
            LinkRecheckClassBarTests.Qualify(db, LinkRecheckRules.AlcoholClass);
            var tier = LinkRecheckRules.CandidateTier(LinkRecheckRules.AlcoholClass);
            var service = Recheck(db);
            var before = await FingerprintAsync(db);

            cutOff.Armed = true;
            await FluentActions.Awaiting(() => service.RunLaneBatchAsync(LinkRecheckRules.Alcohol, LaneMode.Live, 100)).Should().ThrowAsync<OperationCanceledException>();
            (await FingerprintAsync(db)).Should().NotBe(before, "one line had moved before the cut");

            var status = await service.StatusAsync(enabled: false);
            var run = status.LastRuns.Should().ContainSingle(r => r.Set == tier).Subject;
            run.Complete.Should().BeFalse();
            (await service.UndoAsync(run.Stamp)).Found.Should().BeTrue();
            (await new LinkRecheckLaneUndo(service, db).UndoSinceAsync(tier, run.Stamp.AddSeconds(-1), apply: true, CancellationToken.None)).Undone.Should().BeGreaterThan(0);
            (await FingerprintAsync(db)).Should().Be(before);
        }

        [Fact]
        public async Task The_time_window_relink_undo_leaves_the_rechecks_line_moves_to_the_rechecks_own_undo()
        {
            await using var db = NewContext();
            var bar = await BarAsync(db);
            var service = Recheck(db);
            var before = await FingerprintAsync(db);
            var run = await service.RunAsync(LinkRecheckRules.Alcohol, apply: true);

            var relinkUndo = await new LineRelinkService(db, new NoUser()).UndoAsync(run!.Stamp!.Value.AddMinutes(-1), apply: true);
            relinkUndo.Restored.Should().Be(0);
            (await FoodOfAsync(db, bar.Trifle.Id, "1 glass of sherry")).Should().Be(bar.DrySherry.Id, "the line and the trusted alias on the sherry go back together, by the re-check's stamp");

            (await service.UndoAsync(run.Stamp.Value, apply: true)).LinesRestored.Should().Be(3);
            (await FingerprintAsync(db)).Should().Be(before);
        }

        [Fact]
        public async Task A_wording_the_recheck_moved_is_read_again_by_the_later_sets_and_its_alias_goes_when_its_lines_leave()
        {
            await using var db = NewContext();
            var skins = Food("Raw Potato Skins", "Potatoes, raw, skin", "170032", 11);
            var potatoes = Food("Potatoes", "Potatoes, flesh and skin, raw", "170026", 11);
            var chorizo = Food("Chorizo", "Chorizo, pork and beef", "174580", 7);
            db.Ingredients.AddRange(skins, potatoes, chorizo);
            await db.SaveChangesAsync();
            db.IngredientAliases.AddRange(
                new IngredientAliasEntity { IngredientId = skins.Id, AliasName = "large potatoes", SourceContext = LinkRecheckService.MovedAliasPrefix + LinkRecheckRules.NameRuleTier },
                new IngredientAliasEntity { IngredientId = chorizo.Id, AliasName = "ground pork sausage", SourceContext = LinkRecheckService.MovedAliasPrefix + LinkRecheckRules.NameRuleTier });
            var soup = Scraped("Potato soup", (skins, "4 large potatoes"), (chorizo, "1 lb ground pork sausage"));
            db.Recipes.Add(soup);
            await db.SaveChangesAsync();
            var service = Recheck(db);

            var aliases = await service.RunAsync(LinkRecheckRules.Aliases);
            aliases!.Samples.Should().Contain(s => s.FoodId == chorizo.Id && s.Wording == "ground pork sausage" && s.Lines == 1, "a trusted alias the re-check gave is rule-given too");

            LinkRecheckClassBarTests.Qualify(db, LinkGate.Part);
            var run = await service.RunLaneBatchAsync(LinkRecheckRules.PartHerb, LaneMode.Live, 50000);
            (await FoodOfAsync(db, soup.Id, "4 large potatoes")).Should().NotBe(skins.Id);
            (await db.IngredientAliases.IgnoreQueryFilters().SingleAsync(a => a.IngredientId == skins.Id && a.AliasName == "large potatoes")).IsDeleted
                .Should().BeTrue("the re-check's own alias stops teaching imports the skins once its lines leave");
            run.AliasesUntrusted.Should().BeGreaterThan(0);
        }

        [Fact]
        public async Task The_prepared_set_takes_lines_off_a_prepared_food_even_where_a_verified_row_or_a_persons_alias_gives_it_and_lists_the_row()
        {
            await using var db = NewContext();
            var taquitos = Food("Beef Taquitos", "Fast foods, taquitos, beef", "171999", 25);
            var nuggets = Food("Chicken Nuggets", "Fast foods, chicken nuggets", "171998", 25);
            db.Ingredients.AddRange(taquitos, nuggets);
            await db.SaveChangesAsync();
            var row = new IngredientWordingEntity { Wording = "taquitos", Verdict = IngredientWordingVerdict.Food, IngredientId = taquitos.Id, Batch = "w01", Source = "test" };
            db.IngredientWordings.Add(row);
            db.IngredientAliases.Add(new IngredientAliasEntity { IngredientId = nuggets.Id, AliasName = "nuggets", SourceContext = IngredientAliasTrust.AdminPrefix + "7" });
            var party = Scraped("Party", (taquitos, "12 taquitos"), (nuggets, "20 nuggets"));
            db.Recipes.Add(party);
            await db.SaveChangesAsync();

            var run = await Recheck(db).RunAsync(LinkRecheckRules.Prepared, apply: true);
            run!.Verified.Should().Be(0, "a prepared food is never a recipe line's food, whoever gave it");
            run.LinesUnlinked.Should().Be(2);
            (await FoodOfAsync(db, party.Id, "12 taquitos")).Should().NotBe(taquitos.Id);
            var nuggetsNow = await FoodOfAsync(db, party.Id, "20 nuggets");
            nuggetsNow.Should().NotBe(nuggets.Id, "a line is never left on a food the rules prove wrong (staging checkpoint 7, S15)");
            (await db.IngredientAliases.AnyAsync(a => a.IngredientId == nuggetsNow && a.SourceContext == RecheckDietGuard.UnknownContentsContext)).Should().BeTrue(
                "\"nuggets\" names no contents, and taking the line off chicken nuggets would lower the meat types' flag (staging checkpoint 4, P19), so its food is of unknown contents");
            (run.Held, run.UnknownGroups, run.FoodsMarkedUnknown).Should().Be((0, 1, 1));
            run.ByReason.Should().ContainKey(RecheckDietGuard.UnknownReason);
            run.WordingRows.Should().Equal(new[] { row.Id }, "its retract is the operator's next step, so imports stop landing on the prepared food");
        }

        [Fact]
        public async Task A_take_back_that_would_lower_a_diet_flag_goes_to_a_food_of_unknown_contents_that_a_severe_type_holds_and_its_undo_puts_it_back()
        {
            await using var db = NewContext();
            var juice = Food("Orange Juice", "Orange juice, raw", "169098", 9);
            var cider = Food("Apple Cider Vinegar", "Vinegar, cider", "173469", 2);
            db.Ingredients.AddRange(juice, cider);
            await db.SaveChangesAsync();
            var duck = Scraped("Duck with orange", (juice, "1 glass Grand Marnier"));
            var punch = Scraped("Punch", (cider, "1 qt hard cider"));
            db.Recipes.AddRange(duck, punch);
            await db.SaveChangesAsync();

            var before = await FingerprintAsync(db);
            var dry = await Recheck(db).RunAsync(LinkRecheckRules.Alcohol);
            dry!.Samples.Should().Contain(s => s.Outcome == LinkRecheckRules.Unlink && s.FoodId == juice.Id && s.Reason!.StartsWith(RecheckDietGuard.UnknownReason) && s.Reason.Contains("Citrus Allergy"),
                "an orange liqueur taken off orange juice to its own wording would lower Citrus Allergy, so its new food is of unknown contents");
            var run = await Recheck(db).RunAsync(LinkRecheckRules.Alcohol, apply: true);
            (run!.LinesUnlinked, run.UnknownGroups, run.FoodsMarkedUnknown, run.Held).Should().Be((2, 2, 2, 0));
            (dry.UnknownGroups, dry.FoodsMarkedUnknown).Should().Be((run.UnknownGroups, run.FoodsMarkedUnknown));
            var marnier = await FoodOfAsync(db, duck.Id, "1 glass Grand Marnier");
            marnier.Should().NotBe(juice.Id, "staging checkpoint 7, S15: a hold kept the line on a food the rules prove wrong");
            (await FoodOfAsync(db, punch.Id, "1 qt hard cider")).Should().NotBe(cider.Id);

            const long citrus = 61001, household = 61002;
            db.Set<Nom.Data.Reference.ReferenceEntity>().Add(new Nom.Data.Reference.ReferenceEntity { Id = citrus, Name = "Citrus Allergy" });
            db.HouseholdMembers.Add(new Nom.Data.Plan.HouseholdMemberEntity { HouseholdId = household, PersonId = household, IsActive = true });
            db.Restrictions.Add(new Nom.Data.Plan.RestrictionEntity { PersonId = household, Name = "Citrus Allergy", RestrictionTypeId = citrus });
            await db.SaveChangesAsync();
            await DefaultRestrictionCriteria.EnsureAsync(db);
            await new DietGateStoreBuilder().RunAsync(db);
            var set = await new HouseholdRestrictionResolver(db).ResolveAsync(household);
            set.Stored!.UnknownSetIds.Should().NotBeEmpty("an allergy holds unknowns");
            (await db.Recipes.WithoutHeld(db, set).Select(r => r.Id).ToListAsync()).Should().NotContain(duck.Id, "Citrus Allergy holds the recipe as unknown, not shown as if the liqueur were free of orange");
            (await db.DietRecipeFlags.AnyAsync(f => f.RecipeId == duck.Id && f.Verdict == Nom.Data.Plan.DietVerdict.Unknown && f.Reason == Nom.Data.Plan.DietFlagReason.UnknownFood && f.IngredientId == marnier)).Should().BeTrue();

            var dryUndo = await Recheck(db).UndoAsync(run.Stamp!.Value);
            var held = await Recheck(db).UndoAsync(run.Stamp!.Value, apply: true);
            (held.LinesRestored, held.HeldByDiet, held.MarksKept, held.FoodsRemoved).Should().Be((0, 2, 2, 0),
                "by default an undo never lowers a flag, and a line off a food of unknown contents lowers every type its old food does not flag, so the lines and their foods' marks stay (review ck16-r1)");
            (dryUndo.LinesRestored, dryUndo.HeldByDiet, dryUndo.MarksKept, dryUndo.FoodsRemoved).Should().Be((held.LinesRestored, held.HeldByDiet, held.MarksKept, held.FoodsRemoved), "the dry run counts what the apply does");
            (await FoodOfAsync(db, duck.Id, "1 glass Grand Marnier")).Should().Be(marnier);
            (await db.IngredientAliases.CountAsync(a => a.SourceContext == RecheckDietGuard.UnknownContentsContext)).Should().Be(2);

            var undo = await Recheck(db).UndoAsync(run.Stamp!.Value, apply: true, restoreLowered: true);
            (undo.LinesRestored, undo.RestoredLowering).Should().Be((2, 2));
            (await FingerprintAsync(db)).Should().Be(before, "by the operator's explicit choice the undo puts the lines back and removes the foods it made with their marks");
            (await db.IngredientAliases.AnyAsync(a => a.SourceContext == RecheckDietGuard.UnknownContentsContext)).Should().BeFalse();
        }

        [Fact]
        public async Task A_move_off_a_food_of_unknown_contents_to_a_food_no_severe_type_flags_is_held_by_the_move_guard()
        {
            await using var db = NewContext();
            var wine = Food("Dry Sherry");
            var marked = Food("shaoxing wine");
            db.Ingredients.AddRange(wine, marked);
            await db.SaveChangesAsync();
            db.IngredientAliases.Add(new IngredientAliasEntity { IngredientId = marked.Id, AliasName = "shaoxing wine", SourceContext = RecheckDietGuard.UnknownContentsContext });
            var stirfry = Scraped("Stir-fry", (marked, "2 tablespoons shaoxing wine"));
            db.Recipes.Add(stirfry);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var guard = await LineMoveDietGuard.LoadAsync(db);
            (await guard.LoweredAsync(marked.Id, wine.Id, "2 tablespoons shaoxing wine")).Should().Contain(new[] { "Celiac Disease", "Wheat Allergy" },
                "the store holds a recipe on a food of unknown contents as unknown for every set that holds unknowns, so taking the line to a food Celiac Disease does not flag lowers it (review ck16-r1)");
            (await guard.LoweredAsync(wine.Id, marked.Id, "2 tablespoons shaoxing wine")).Should().BeEmpty("a move onto a food of unknown contents raises");

            var moved = await new LineRelinkService(db, new NoUser()).MoveFoodAsync(marked.Id, wine.Id);
            (moved!.Moved, moved.HeldByDiet).Should().Be((0, 1));
            (await FoodOfAsync(db, stirfry.Id, "2 tablespoons shaoxing wine")).Should().Be(marked.Id);
        }

        [Fact]
        public async Task A_move_to_a_curated_food_whose_name_would_lower_a_diet_flag_goes_to_a_food_of_unknown_contents_and_a_move_that_lowers_none_goes_ahead()
        {
            await using var db = NewContext();
            var burger = Food("Beef Hamburger");
            var patty = Food("Patty");
            var canola = Food("Canola Oil");
            var olive = Food("Olive Oil");
            db.Ingredients.AddRange(burger, patty, canola, olive);
            await db.SaveChangesAsync();
            foreach (var (wording, food) in new[] { ("patty", burger), ("olive oil", canola) })
            {
                var pick = Pick(null, wording, food.Id, FoodProposalStatus.Applied);
                pick.ReviewedAt = LinkRecheckRules.NovemberThird.AddHours(22);
                db.FoodCatalogProposals.Add(pick);
            }
            var supper = Scraped("Supper", (burger, "1 patty"), (canola, "2 tbsp olive oil"));
            db.Recipes.Add(supper);
            await db.SaveChangesAsync();
            LinkRecheckClassBarTests.Qualify(db, LinkGate.OtherFood);

            var run = await Recheck(db).RunLaneBatchAsync(LinkRecheckRules.AppliedNovember, LaneMode.Live, 50000);
            (run.Held, run.UnknownGroups).Should().Be((0, 1));
            run.ByReason.Should().ContainKey(RecheckDietGuard.UnknownReason);
            var now = await FoodOfAsync(db, supper.Id, "1 patty");
            now.Should().NotBe(burger.Id).And.NotBe(patty.Id, "a promoted move to a food named only \"Patty\" would lower the meat types' flag the beef hamburger gave (review CK9T-2), so the line goes to a food of unknown contents");
            (await db.IngredientAliases.AnyAsync(a => a.IngredientId == now && a.SourceContext == RecheckDietGuard.UnknownContentsContext)).Should().BeTrue();
            (await FoodOfAsync(db, supper.Id, "2 tbsp olive oil")).Should().Be(olive.Id);
        }

        [Fact]
        public async Task A_shadow_batch_writes_no_run_header_so_the_status_keeps_the_operators_run_and_the_lanes_undo_has_nothing_to_take()
        {
            await using var db = NewContext();
            await BarAsync(db);
            var service = Recheck(db);
            var applied = await service.RunAsync(LinkRecheckRules.Alcohol, apply: true, max: 1);
            db.LaneStates.RemoveRange(db.LaneStates);
            await db.SaveChangesAsync();

            var shadow = await service.RunLaneBatchAsync(LinkRecheckRules.Alcohol, LaneMode.Shadow, 100);
            shadow.Groups.Should().Be(2);
            shadow.Stamp.Should().BeNull();
            var status = await service.StatusAsync(enabled: true);
            status.LastRuns.Should().ContainSingle(r => r.Set == LinkRecheckRules.Alcohol).Which.Stamp.Should().Be(applied!.Stamp!.Value);
            (await db.AuditLogEntries.CountAsync(a => a.ChangeType == LinkRecheckService.RunChangeType)).Should().Be(1);
            (await new LinkRecheckLaneUndo(service, db).UndoSinceAsync(LaneTiers.Whole, applied.Stamp.Value.AddSeconds(-1), apply: true, CancellationToken.None)).Undone.Should().Be(0);
            (await db.AuditLogEntries.CountAsync(a => a.ChangeType == LinkRecheckService.UndoChangeType)).Should().Be(0);
        }

        [Fact]
        public async Task A_take_back_next_to_a_curated_food_of_the_same_name_in_another_case_lands_on_its_wording_food()
        {
            await using var db = NewContext();
            var tortellini = Food("Cheese Tortellini", "Restaurant, Italian, cheese tortellini", "171234", 25);
            db.Ingredients.Add(tortellini);
            await db.SaveChangesAsync();
            var dinner = Scraped("Dinner", (tortellini, "1 lb cheese tortellini"));
            db.Recipes.Add(dinner);
            await db.SaveChangesAsync();

            var run = await Recheck(db).RunAsync(LinkRecheckRules.Prepared, apply: true);
            (run!.LinesUnlinked, run.NoHome, run.FoodsCreated).Should().Be((1, 0, 1), "the wording food is the uncurated landing an unlinked wording always has; only the very same name is refused, by the unique index");
        }
    }
}

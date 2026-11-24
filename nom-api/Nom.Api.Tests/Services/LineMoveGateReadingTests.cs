using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using Nom.Data;
using Nom.Data.Curation;
using Nom.Data.Nutrient;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// Staging checkpoint 6, R17: the line-move guard reads a move as the diet gate does. A relink by food moved the
    /// "Franco-American Spaghetti" lines off USDA canned pasta with tomato sauce onto Spaghetti (Nightshade and
    /// Interstitial Cystitis lowered, held 0, and the route had no dry run); the w01 unload moved "mayonnaise dressing" off
    /// Mayonnaise and lowered Soy Allergy; and a wrong link approval made Coconut Milk carry the grape tomatoes' flags.
    /// A plain drink line keeps its own drink flags by its words and may leave the wrong food's (R16).
    /// </summary>
    public class LineMoveGateReadingTests
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

        private static IngredientEntity Curated(string name, string? fdcId = null, string? description = null)
        {
            var food = new IngredientEntity
            {
                Name = name,
                FdcId = fdcId,
                CurationStatusId = (long)CurationStatusEnum.Curated,
                IngredientNutrients = new List<IngredientNutrientEntity> { new() { NutrientId = 1, Amount = 1 } },
            };
            if (description != null) food.Aliases.Add(new IngredientAliasEntity { AliasName = description, SourceContext = IngredientLinkService.UsdaDescriptionContext });
            return food;
        }

        private static IngredientEntity Uncurated(string name) => new() { Name = name, CurationStatusId = (long)CurationStatusEnum.NonCurated };

        private static RecipeEntity Scraped(string name, params (IngredientEntity Food, string Line)[] lines) => new()
        {
            Name = name,
            ScrapedAtUtc = DateTime.UtcNow,
            RecipeIngredients = lines.Select((l, i) => new RecipeIngredientEntity
            {
                IngredientId = l.Food.Id, SortOrder = i, Quantity = 1, MeasurementId = 1, RawLine = l.Line,
            }).ToList(),
        };

        private static async Task<long> FoodOfAsync(ApplicationDbContext db, long recipeId)
        {
            db.ChangeTracker.Clear();
            return await db.RecipeIngredients.AsNoTracking().Where(ri => ri.RecipeId == recipeId).Select(ri => ri.IngredientId).FirstAsync();
        }

        [Fact]
        public async Task A_relink_by_food_onto_a_usda_food_that_lowers_a_flag_is_held_in_its_dry_run_and_its_apply()
        {
            using var db = NewContext();
            var canned = Curated("Pasta with tomato sauce, no meat, canned", "172100", "Pasta with tomato sauce, no meat, canned");
            var spaghetti = Curated("Spaghetti", "168927", "Pasta, cooked, enriched, without added salt");
            db.Ingredients.AddRange(canned, spaghetti);
            await db.SaveChangesAsync();
            var supper = Scraped("Supper", (canned, "1 can Franco-American Spaghetti"));
            db.Recipes.Add(supper);
            await db.SaveChangesAsync();

            var dry = await new LineRelinkService(db, new NoUser()).MoveFoodAsync(canned.Id, spaghetti.Id, apply: false);
            var applied = await new LineRelinkService(db, new NoUser()).MoveFoodAsync(canned.Id, spaghetti.Id);

            (dry!.Remaining, dry.HeldByDiet).Should().Be((0, 1), "the dry run counts what the apply holds and writes nothing");
            dry.DietHolds.Single().Types.Should().Contain("Nightshade Sensitivity");
            (applied!.Moved, applied.HeldByDiet).Should().Be((0, 1), "a USDA target is no longer trusted for its description: the gate reads Spaghetti as no tomato");
            (await FoodOfAsync(db, supper.Id)).Should().Be(canned.Id);
        }

        [Fact]
        public async Task A_move_the_gate_reads_as_lowering_soy_is_lowering_although_the_wording_is_a_known_composite()
        {
            using var db = NewContext();
            var mayonnaise = Curated("Mayonnaise", "171009", "Salad dressing, mayonnaise, soybean oil, with salt");
            var dressing = Uncurated("Aioli");
            db.Ingredients.AddRange(mayonnaise, dressing);
            await db.SaveChangesAsync();
            var guard = await LineMoveDietGuard.LoadAsync(db);

            (await guard.LoweredAsync(mayonnaise.Id, dressing.Id, "1/2 cup aioli")).Should().Contain("Soy Allergy",
                "the gate reads the soybean oil in the USDA description and nothing in the wording food");
            (await guard.LoweredToNameAsync(mayonnaise.Id, "aioli", "1/2 cup aioli")).Should().Contain("Soy Allergy");
            (await guard.LoweredAsync(mayonnaise.Id, mayonnaise.Id, "1/2 cup aioli")).Should().BeEmpty();
            (await guard.LoweredToNameAsync(mayonnaise.Id, "mayonnaise dressing", "1/2 cup mayonnaise dressing")).Should().NotContain("Soy Allergy",
                "the gate now reads mayonnaise itself as soy (diet-vocabulary.tsv, ck11-diet-coverage), so the wording food keeps the flag");
        }

        [Fact]
        public async Task A_plain_drink_line_leaves_a_wrong_foods_flags_but_a_liqueur_or_a_drink_that_shares_the_foods_word_keeps_them()
        {
            using var db = NewContext();
            var liqueur = Curated("Coffee Cream Liqueur", "175098", "Alcoholic beverage, liqueur, coffee with cream, 34 proof");
            var spirit = Curated("Distilled Spirit", "174815", "Alcoholic beverage, distilled, all (gin, rum, vodka, whiskey) 80 proof");
            var juice = Curated("Orange Juice", "169098", "Orange juice, raw");
            var cherries = Curated("Cherries", "171719", "Cherries, sweet, raw");
            db.Ingredients.AddRange(liqueur, spirit, juice, cherries);
            await db.SaveChangesAsync();
            var guard = await LineMoveDietGuard.LoadAsync(db);

            (await guard.LoweredAsync(liqueur.Id, spirit.Id, "1 gill brandy")).Should().BeEmpty("brandy has no cream and no coffee");
            (await guard.LoweredToNameAsync(liqueur.Id, "brandy", "1 gill brandy")).Should().BeEmpty();
            (await guard.LoweredToNameAsync(liqueur.Id, "elderflower liqueur", "2 tablespoons elderflower liqueur")).Should().Contain("Milk Allergy",
                "a liqueur is flavoured with what its name may not say");
            (await guard.LoweredToNameAsync(juice.Id, "grand marnier", "1 glass Grand Marnier")).Should().Contain("Citrus Allergy");
            RecheckDietGuard.ReadsAsDrink("1 gill cherry brandy", "Cherries Cherries, sweet, raw").Should().BeFalse("it shares the word cherry with the food it leaves");
            RecheckDietGuard.ReadsAsDrink("1/2 cup Madeira or sherry", "Red Wine Vinegar Vinegar, red wine").Should().BeTrue();
            RecheckDietGuard.ReadsAsDrink("2 tbsp red wine vinegar").Should().BeFalse();
        }

        [Fact]
        public async Task A_link_approval_that_would_make_its_target_carry_the_wordings_flags_stays_pending_and_its_dry_run_says_why()
        {
            using var db = NewContext();
            var tomatoes = Uncurated("grape tomatoes, sliced in half");
            var coconut = Curated("Coconut Milk", "170172", "Nuts, coconut milk, raw (liquid expressed from grated meat and water)");
            db.Ingredients.AddRange(tomatoes, coconut);
            await db.SaveChangesAsync();
            var salad = Scraped("Salad", (tomatoes, "1 cup grape tomatoes, sliced in half"));
            var curry = Scraped("Curry", (coconut, "1 can coconut milk"));
            db.Recipes.AddRange(salad, curry);
            var proposal = new FoodCatalogProposalEntity
            {
                Action = FoodProposalAction.Update, IngredientId = tomatoes.Id, Field = FoodCatalogReviewService.FdcLinkField, CurrentValue = tomatoes.Name,
                FdcId = coconut.FdcId, ProposedValue = coconut.Id.ToString(CultureInfo.InvariantCulture), Source = "ai:fake", Batch = IngredientLinkService.Batch,
                Status = FoodProposalStatus.Pending,
            };
            db.FoodCatalogProposals.Add(proposal);
            await db.SaveChangesAsync();
            var review = new FoodCatalogReviewService(db, new Mock<IFoodCatalogAuditService>().Object, new CatalogCleanupService(db, new NoUser()));

            var preview = await review.PreviewProposalAsync(proposal.Id, reviewerPersonId: 2);
            var applied = await review.ApplyProposalAsync(proposal.Id, reviewerPersonId: 2);

            preview!.Applies.Should().BeFalse();
            preview.Lines.Should().Be(1);
            preview.HeldByDiet.Should().Be(0, "the line names its tomatoes itself, so moving it lowers nothing");
            preview.RaisedTypes.Should().Contain("Nightshade Sensitivity", "the wording's name kept on Coconut Milk would make every coconut milk recipe a nightshade recipe");
            applied.Should().BeFalse();
            db.ChangeTracker.Clear();
            (await db.FoodCatalogProposals.SingleAsync()).Status.Should().Be(FoodProposalStatus.Pending);
            (await FoodOfAsync(db, salad.Id)).Should().Be(tomatoes.Id);
            (await db.IngredientAliases.AnyAsync(a => a.IngredientId == coconut.Id && a.AliasName.Contains("tomato"))).Should().BeFalse();
        }
    }
}

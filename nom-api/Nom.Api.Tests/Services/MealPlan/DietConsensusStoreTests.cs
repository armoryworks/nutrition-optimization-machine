using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Nom.Data;
using Nom.Data.Curation;
using Nom.Data.Plan;
using Nom.Data.Recipe;
using Nom.Data.Reference;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Services.MealPlan
{
    /// <summary>
    /// The two catalog facts staging checkpoint 7's diet proof left shown (draw 14): a curated condensed cream soup read
    /// clear for Wheat Allergy, now flagged by the branded consensus of the committed file; and "Protose", the vegetarian
    /// books' wheat-gluten and nut meat, linked by a model pick to Fresh Coconut Meat, now read by its words and so not
    /// understood, while the same food's lines that name what it is, and a link a verified wording proves, are read as before.
    /// </summary>
    public class DietConsensusStoreTests
    {
        private const long WheatRef = 60830, PeanutRef = 60820, Gram = 1;

        private static ApplicationDbContext NewContext() =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
                new HttpContextAccessor { HttpContext = new DefaultHttpContext() });

        private static IngredientEntity Curated(string name, int category, string description, string? fdcId = null) =>
            new() { Name = name, FdcId = fdcId, FdcDataType = "sr_legacy_food", FdcCategoryId = category, CurationStatusId = (long)CurationStatusEnum.Curated, Aliases = new List<IngredientAliasEntity> { new() { AliasName = description, SourceContext = IngredientLinkService.UsdaDescriptionContext } } };

        private static RecipeEntity Recipe(string name, params (long Food, string Raw)[] lines)
        {
            var recipe = new RecipeEntity { Name = name, AuthorId = 70, CurationStatusId = (long)CurationStatusEnum.NonCurated, RecipeIngredients = new List<RecipeIngredientEntity>() };
            var order = 0;
            foreach (var (food, raw) in lines)
                recipe.RecipeIngredients.Add(new RecipeIngredientEntity { IngredientId = food, MeasurementId = Gram, Quantity = 1m, RawLine = raw, SortOrder = order++ });
            return recipe;
        }

        private static async Task PersonAsync(ApplicationDbContext db, long household, long person, string type, long typeId)
        {
            db.HouseholdMembers.Add(new HouseholdMemberEntity { HouseholdId = household, PersonId = person, IsActive = true });
            db.Set<ReferenceEntity>().Add(new ReferenceEntity { Id = typeId, Name = type });
            db.Restrictions.Add(new RestrictionEntity { PersonId = person, Name = type, RestrictionTypeId = typeId });
            await db.SaveChangesAsync();
        }

        private static async Task<HashSet<long>> OfferedAsync(ApplicationDbContext db, long person) =>
            (await db.Recipes.WithoutHeld(db, await new HouseholdRestrictionResolver(db).ResolveSevereForPersonAsync(person)).Select(r => r.Id).ToListAsync()).ToHashSet();

        [Fact]
        public async Task A_condensed_cream_soup_is_flagged_for_wheat_by_the_consensus_of_its_brands()
        {
            using var db = NewContext();
            await PersonAsync(db, 7, 70, "Wheat Allergy", WheatRef);
            var soup = Curated("Condensed Cream Of Chicken Soup", 6, "Soup, cream of chicken, canned, condensed", "171146");
            var rice = Curated("Rice", 20, "Rice, white, long-grain, regular, raw, enriched");
            db.Ingredients.AddRange(soup, rice);
            await db.SaveChangesAsync();
            var curried = Recipe("Curried Chicken Soup", (soup.Id, "1 can condensed cream of chicken soup"), (rice.Id, "1 cup rice"));
            var plain = Recipe("Boiled Rice", (rice.Id, "1 cup rice"));
            db.Recipes.AddRange(curried, plain);
            await db.SaveChangesAsync();
            await DefaultRestrictionCriteria.EnsureAsync(db);
            await new DietGateStoreBuilder().RunAsync(db);

            var offered = await OfferedAsync(db, 70);
            offered.Should().NotContain(curried.Id, "109 of 113 branded condensed cream of chicken soups state wheat (diet-branded-consensus.tsv)");
            offered.Should().Contain(plain.Id);
            (await db.DietFoodFacts.Where(f => f.IngredientId == soup.Id).Select(f => f.Source).ToListAsync()).Should().Contain(BrandedConsensus.FactSource);
        }

        [Fact]
        public async Task A_trade_name_no_rule_knows_on_a_model_link_is_not_read_as_the_food_the_link_sits_on()
        {
            using var db = NewContext();
            await PersonAsync(db, 9, 90, "Peanut Allergy", PeanutRef);
            var coconut = Curated("Fresh Coconut Meat", 12, "Nuts, coconut meat, raw");
            var potato = Curated("Potatoes", 11, "Potatoes, flesh and skin, raw");
            db.Ingredients.AddRange(coconut, potato);
            await db.SaveChangesAsync();
            db.FoodCatalogProposals.AddRange(
                new FoodCatalogProposalEntity { Field = FoodCatalogReviewService.FdcLinkField, CurrentValue = "Protose, ½ pound", ProposedValue = coconut.Id.ToString(), Source = "ai:qwen2.5:14b-instruct", Status = FoodProposalStatus.Applied, Batch = "fdc-link" },
                new FoodCatalogProposalEntity { Field = FoodCatalogReviewService.FdcLinkField, CurrentValue = "grated coconut", ProposedValue = coconut.Id.ToString(), Source = "ai:qwen2.5:14b-instruct", Status = FoodProposalStatus.Applied, Batch = "fdc-link" });
            var hash = Recipe("Protose Hash", (coconut.Id, "Protose, ½ pound."), (potato.Id, "2 cups potatoes"));
            var macaroons = Recipe("Coconut Drops", (coconut.Id, "grated coconut"));
            var unlinked = Recipe("Copra Cakes", (coconut.Id, "copra, 1 cup"));
            db.Recipes.AddRange(hash, macaroons, unlinked);
            await db.SaveChangesAsync();
            await DefaultRestrictionCriteria.EnsureAsync(db);
            var builder = new DietGateStoreBuilder();
            await builder.RunAsync(db);

            var offered = await OfferedAsync(db, 90);
            offered.Should().NotContain(hash.Id, "\"protose\" is no word of the curated catalog and the link to coconut meat is an unproven model pick");
            offered.Should().Contain(macaroons.Id, "a line on the same model link that names what the food is is read by the food");
            offered.Should().Contain(unlinked.Id, "a link no unproven lane made is read by the food, as before");

            db.IngredientWordings.Add(new IngredientWordingEntity { Wording = "protose, ½ pound", Verdict = IngredientWordingVerdict.Food, IngredientId = coconut.Id, Batch = "test", Source = "verified:test" });
            await db.SaveChangesAsync();
            await builder.RunChangesAsync(db);
            (await OfferedAsync(db, 90)).Should().Contain(hash.Id, "a verified wording proves the link, so the line is read by its food again");
        }

        [Fact]
        public async Task A_vegan_analogue_read_by_its_words_is_not_read_as_the_food_it_imitates()
        {
            using var db = NewContext();
            await PersonAsync(db, 9, 90, "Tree Nut Allergy", PeanutRef);
            var parmesan = Curated("Parmesan Cheese", 1, "Cheese, parmesan, hard");
            var squash = Curated("Yellow Squash", 11, "Squash, summer, crookneck and straightneck, raw");
            var vegan = new IngredientEntity { Name = "vegan parmesan", CurationStatusId = (long)CurationStatusEnum.NonCurated };
            var plain = new IngredientEntity { Name = "grated parmesan", CurationStatusId = (long)CurationStatusEnum.NonCurated };
            db.Ingredients.AddRange(parmesan, squash, vegan, plain);
            await db.SaveChangesAsync();
            var analogue = Recipe("Sauteed Yellow Squash", (squash.Id, "3 yellow squash"), (vegan.Id, "1/4 cup Vegan Parmesan"));
            var cheese = Recipe("Squash Gratin", (squash.Id, "3 yellow squash"), (plain.Id, "1/4 cup grated parmesan"));
            db.Recipes.AddRange(analogue, cheese);
            await db.SaveChangesAsync();
            await DefaultRestrictionCriteria.EnsureAsync(db);
            await new DietGateStoreBuilder().RunAsync(db);

            var offered = await OfferedAsync(db, 90);
            offered.Should().NotContain(analogue.Id, "a vegan parmesan is commonly made of cashews; its words name the cheese it imitates");
            offered.Should().Contain(cheese.Id);
        }

        [Theory]
        [InlineData("Protose, ½ pound.", true)]
        [InlineData("protose", true)]
        [InlineData("2 cups grated coconut", false)]
        public void A_line_sits_on_an_unproven_link_when_its_wording_is_the_picked_wording(string raw, bool expected)
        {
            var links = DietGateStoreBuilder.UnprovenOf(new[] { (1L, IngredientNameNormalizer.WordingKey("Protose, ½ pound")), (1L, "protose") })[1];
            DietGateStoreBuilder.OnUnprovenLink(raw, links).Should().Be(expected);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Nom.Data;
using Nom.Data.Plan;
using Nom.Data.Recipe;
using Nom.Data.Reference;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Services.MealPlan
{
    /// <summary>
    /// A line whose wording the verified table gives a category (batch w02) is decided for a severe set by what the curated
    /// foods of that group agree on, not by its unknown words: "isinglass" (fish gelatin, Webster 1913) is flagged for Fish
    /// Allergy because every curated "Fish, ..." food is, and shown to Peanut Allergy because none is and Finfish and
    /// Shellfish Products is a category of single commodities; a stray line on the same row whose text is not the wording is
    /// still read by its words, and a new category row decides its recipes again.
    /// </summary>
    public class DietWordingCategoryStoreTests
    {
        private const long FishRef = 60810, PeanutRef = 60820, Gram = 1;

        private static ApplicationDbContext NewContext() =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
                new HttpContextAccessor { HttpContext = new DefaultHttpContext() });

        private static IngredientEntity Curated(string name, int category, string description) =>
            new() { Name = name, FdcCategoryId = category, CurationStatusId = (long)CurationStatusEnum.Curated, Aliases = new List<IngredientAliasEntity> { new() { AliasName = description, SourceContext = IngredientLinkService.UsdaDescriptionContext } } };

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
        public async Task A_category_wording_is_decided_by_what_the_curated_foods_of_its_group_agree_on()
        {
            using var db = NewContext();
            await PersonAsync(db, 7, 70, "Fish Allergy", FishRef);
            await PersonAsync(db, 9, 90, "Peanut Allergy", PeanutRef);
            var cod = Curated("Cod", 15, "Fish, cod, Atlantic, raw");
            var salmon = Curated("Salmon", 15, "Fish, salmon, Atlantic, wild, raw");
            var milk = Curated("Milk", 1, "Milk, whole, 3.25% milkfat");
            var chicken = Curated("Chicken", 5, "Chicken, broilers or fryers, meat only, raw");
            var nuggets = Curated("Chicken nuggets", 5, "Chicken, nuggets, peanut breaded");
            var chickens = new IngredientEntity { Name = "chickens", CurationStatusId = (long)CurationStatusEnum.NonCurated };
            var isinglass = new IngredientEntity { Name = "isinglass", CurationStatusId = (long)CurationStatusEnum.NonCurated };
            db.Ingredients.AddRange(cod, salmon, milk, isinglass, chicken, nuggets, chickens);
            await db.SaveChangesAsync();
            var blancmange = Recipe("Blancmange", (milk.Id, "1 quart of milk"), (isinglass.Id, "1 ounce of isinglass"));
            var stray = Recipe("Calf's Foot Jelly", (milk.Id, "1 pint of milk"), (isinglass.Id, "isinglass dissolved in a gill of rosewater"));
            var chickenDish = Recipe("Boiled Chickens", (chickens.Id, "two chickens"));
            db.Recipes.AddRange(blancmange, stray, chickenDish);
            await db.SaveChangesAsync();
            await DefaultRestrictionCriteria.EnsureAsync(db);
            var builder = new DietGateStoreBuilder();
            await builder.RunAsync(db);

            (await OfferedAsync(db, 90)).Should().NotContain(blancmange.Id, "no curated food knows isinglass, so it is not understood (S3)");

            db.IngredientWordings.Add(new IngredientWordingEntity
            {
                Wording = "isinglass", Verdict = IngredientWordingVerdict.Category, FdcCategoryId = 15, CategoryHead = "Fish", Batch = "w02", Source = "verified:w02",
            });
            db.IngredientWordings.Add(new IngredientWordingEntity
            {
                Wording = "chickens", Verdict = IngredientWordingVerdict.Category, FdcCategoryId = 5, CategoryHead = "Chicken", Batch = "w02", Source = "verified:w02",
            });
            await db.SaveChangesAsync();
            await builder.RunChangesAsync(db);

            var fishSets = (await new HouseholdRestrictionResolver(db).ResolveSevereForPersonAsync(70)).Stored!.SetIds;
            (await OfferedAsync(db, 70)).Should().NotContain(new[] { blancmange.Id, stray.Id });
            (await db.DietRecipeFlags.Where(f => f.RecipeId == blancmange.Id && fishSets.Contains(f.SetId)).ToListAsync())
                .Should().Contain(f => f.Verdict == DietVerdict.Contains && f.Reason == DietFlagReason.FoodWord && f.IngredientId == isinglass.Id,
                    "every curated food of Finfish and Shellfish Products whose description starts \"Fish\" is flagged for Fish Allergy");
            (await db.DietRecipeFlags.Where(f => f.RecipeId == stray.Id && fishSets.Contains(f.SetId)).ToListAsync())
                .Should().Contain(f => f.Verdict == DietVerdict.Unknown && f.Reason == DietFlagReason.UnknownWord,
                    "a line whose own text is not the wording is read by its words, as the verified relink would leave it behind");
            var peanut = await OfferedAsync(db, 90);
            peanut.Should().Contain(chickenDish.Id, "a group mixed for a set adds nothing: the line is read by its words, which a plain curated chicken clears");
            peanut.Should().Contain(blancmange.Id, "no curated fish is flagged for Peanut Allergy and fish is a single commodity");
            peanut.Should().NotContain(stray.Id);
        }
    }
}

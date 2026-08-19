using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Nom.Data;
using Nom.Data.Plan;
using Nom.Data.Recipe;
using Nom.Data.Reference;
using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Services.MealPlan
{
    /// <summary>
    /// A restriction saved from the UI carries only a category (RestrictionTypeId);
    /// planning must still exclude the ingredients that category's criteria describe.
    /// </summary>
    public class HouseholdRestrictionResolverTests
    {
        private const long HouseholdId = 7, PersonId = 70, NutAllergyRef = 2012, VegetarianRef = 2004;

        private static ApplicationDbContext NewContext() =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        private static async Task<(long peanut, long almondMilk, long chicken, long tofu)> SeedAsync(ApplicationDbContext db)
        {
            db.HouseholdMembers.Add(new HouseholdMemberEntity { HouseholdId = HouseholdId, PersonId = PersonId, IsActive = true });
            db.Set<ReferenceEntity>().AddRange(
                new ReferenceEntity { Id = NutAllergyRef, Name = "Nut Allergy" },
                new ReferenceEntity { Id = VegetarianRef, Name = "Vegetarian" });
            var peanut = new IngredientEntity { Name = "Peanut Butter", CurationStatusId = 9003 };
            var almondMilk = new IngredientEntity { Name = "Oat Beverage", CurationStatusId = 9003 };
            almondMilk.Aliases.Add(new IngredientAliasEntity { AliasName = "almond milk" });
            var chicken = new IngredientEntity { Name = "Chicken Thigh", CurationStatusId = 9003 };
            var tofu = new IngredientEntity { Name = "Firm Tofu", CurationStatusId = 9003 };
            db.Ingredients.AddRange(peanut, almondMilk, chicken, tofu);
            await db.SaveChangesAsync();
            return (peanut.Id, almondMilk.Id, chicken.Id, tofu.Id);
        }

        [Fact]
        public async Task Category_restriction_resolves_through_default_criteria_including_aliases()
        {
            using var db = NewContext();
            var (peanut, almondMilk, chicken, tofu) = await SeedAsync(db);
            db.Restrictions.Add(new RestrictionEntity { PersonId = PersonId, Name = "Nut Allergy", RestrictionTypeId = NutAllergyRef });
            await db.SaveChangesAsync();
            (await DefaultRestrictionCriteria.EnsureAsync(db)).Should().BeGreaterThan(0);

            var set = await new HouseholdRestrictionResolver(db).ResolveAsync(HouseholdId);

            set.IngredientIds.Should().Contain(peanut, "peanut matches %peanut%");
            set.IngredientIds.Should().Contain(almondMilk, "the alias 'almond milk' matches %almond%");
            set.IngredientIds.Should().NotContain(chicken).And.NotContain(tofu);
            set.HasSevere.Should().BeTrue("allergy criteria are severity 5");
        }

        [Fact]
        public async Task Diet_restriction_is_not_severe_and_direct_ingredient_ids_still_count()
        {
            using var db = NewContext();
            var (peanut, _, chicken, tofu) = await SeedAsync(db);
            db.Restrictions.Add(new RestrictionEntity { PersonId = PersonId, Name = "Vegetarian", RestrictionTypeId = VegetarianRef });
            db.Restrictions.Add(new RestrictionEntity { PersonId = PersonId, Name = "No tofu", IngredientId = tofu, Severity = 2 });
            await db.SaveChangesAsync();
            await DefaultRestrictionCriteria.EnsureAsync(db);

            var set = await new HouseholdRestrictionResolver(db).ResolveAsync(HouseholdId);

            set.IngredientIds.Should().Contain(chicken).And.Contain(tofu).And.NotContain(peanut);
            set.HasSevere.Should().BeFalse();
        }

        [Fact]
        public async Task Ensure_is_idempotent_and_respects_existing_admin_criteria()
        {
            using var db = NewContext();
            await SeedAsync(db);
            db.Set<RestrictionCriterionEntity>().Add(new RestrictionCriterionEntity { RestrictionTypeId = NutAllergyRef, IngredientPattern = "%custom%", Severity = 5 });
            await db.SaveChangesAsync();

            var first = await DefaultRestrictionCriteria.EnsureAsync(db);
            var second = await DefaultRestrictionCriteria.EnsureAsync(db);

            second.Should().Be(0);
            (await db.Set<RestrictionCriterionEntity>().CountAsync(c => c.RestrictionTypeId == NutAllergyRef)).Should().Be(1, "an admin-edited type is left alone");
            (await db.Set<RestrictionCriterionEntity>().CountAsync(c => c.RestrictionTypeId == VegetarianRef)).Should().Be(first);
        }
    }
}

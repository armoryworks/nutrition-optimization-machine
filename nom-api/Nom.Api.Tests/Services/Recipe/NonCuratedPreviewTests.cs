using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Nom.Data;
using Nom.Data.Person;
using Nom.Data.Platform;
using Nom.Data.Recipe;
using Nom.Orch.Extensions;
using Xunit;

namespace Nom.Api.Tests.Services.Recipe
{
    /// <summary>
    /// The repaired scraped library (public, vetted, not yet curated) is visible only to curation
    /// admins and only while the platform switch is on; it never reaches anyone else, and recipes
    /// still failing vetting stay hidden from everyone.
    /// </summary>
    public class NonCuratedPreviewTests
    {
        private const long Admin = 1, Cook = 2;

        private static async Task<ApplicationDbContext> SeedAsync(bool switchOn)
        {
            var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            db.Persons.AddRange(
                new PersonEntity { Id = Admin, Name = "Admin", UserId = "u-admin" },
                new PersonEntity { Id = Cook, Name = "Cook", UserId = "u-cook" },
                new PersonEntity { Id = 99, Name = "Harvester" });
            db.Set<Nom.Data.Reference.ReferenceEntity>().AddRange(
                new Nom.Data.Reference.ReferenceEntity { Id = (long)CurationStatusEnum.NonCurated, Name = "Non-Curated" },
                new Nom.Data.Reference.ReferenceEntity { Id = (long)CurationStatusEnum.RequiresRevision, Name = "Requires Revision" });
            db.UserClaims.Add(new IdentityUserClaim<string> { UserId = "u-admin", ClaimType = "CanManageCuration", ClaimValue = "true" });
            db.PlatformFeatures.Add(new PlatformFeatureEntity { Key = RecipeVisibilityExtensions.NonCuratedPreviewFeature, IsEnabled = switchOn });
            db.Recipes.AddRange(
                new RecipeEntity { Id = 10, Name = "Vetted scrape", Visibility = RecipeVisibilityEnum.Public, CurationStatusId = (long)CurationStatusEnum.NonCurated, AuthorId = 99 },
                new RecipeEntity { Id = 11, Name = "Flagged scrape", Visibility = RecipeVisibilityEnum.Public, CurationStatusId = (long)CurationStatusEnum.RequiresRevision, AuthorId = 99 });
            await db.SaveChangesAsync();
            return db;
        }

        private static Task<long[]> VisibleIds(ApplicationDbContext db, long? personId) =>
            db.Recipes.VisibleTo(db, personId).Select(r => r.Id).OrderBy(i => i).ToArrayAsync();

        [Fact]
        public async Task Admins_see_vetted_uncurated_recipes_while_the_switch_is_on()
        {
            using var db = await SeedAsync(switchOn: true);
            (await VisibleIds(db, Admin)).Should().Equal(10);
            (await VisibleIds(db, Cook)).Should().BeEmpty();
            (await VisibleIds(db, null)).Should().BeEmpty();
        }

        [Fact]
        public async Task Switch_off_hides_them_from_admins_too()
        {
            using var db = await SeedAsync(switchOn: false);
            (await VisibleIds(db, Admin)).Should().BeEmpty();
        }
    }
}

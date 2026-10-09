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
                new Nom.Data.Reference.ReferenceEntity { Id = (long)CurationStatusEnum.RequiresRevision, Name = "Requires Revision" },
                new Nom.Data.Reference.ReferenceEntity { Id = (long)CurationStatusEnum.Curated, Name = "Approved" });
            db.UserClaims.Add(new IdentityUserClaim<string> { UserId = "u-admin", ClaimType = "CanManageCuration", ClaimValue = "true" });
            db.PlatformFeatures.Add(new PlatformFeatureEntity { Key = RecipeVisibilityExtensions.NonCuratedPreviewFeature, IsEnabled = switchOn });
            db.Recipes.AddRange(
                new RecipeEntity { Id = 10, Name = "Vetted scrape", Visibility = RecipeVisibilityEnum.Public, CurationStatusId = (long)CurationStatusEnum.NonCurated, AuthorId = 99 },
                new RecipeEntity { Id = 11, Name = "Flagged scrape", Visibility = RecipeVisibilityEnum.Public, CurationStatusId = (long)CurationStatusEnum.RequiresRevision, AuthorId = 99 },
                new RecipeEntity { Id = 12, Name = "Approved, rewritten", Visibility = RecipeVisibilityEnum.Public, CurationStatusId = (long)CurationStatusEnum.Curated, AuthorId = 99 },
                new RecipeEntity { Id = 13, Name = "Approved, still source prose", Visibility = RecipeVisibilityEnum.Public, CurationStatusId = (long)CurationStatusEnum.Curated, ContainsSourceProse = true, AuthorId = 99 },
                new RecipeEntity { Id = 14, Name = "Cook's own import", Visibility = RecipeVisibilityEnum.Public, CurationStatusId = (long)CurationStatusEnum.NonCurated, ContainsSourceProse = true, AuthorId = Cook });
            await db.SaveChangesAsync();
            return db;
        }

        private static Task<long[]> VisibleIds(ApplicationDbContext db, long? personId) =>
            db.Recipes.VisibleTo(db, personId).Select(r => r.Id).OrderBy(i => i).ToArrayAsync();

        [Fact]
        public async Task Admins_see_vetted_uncurated_recipes_while_the_switch_is_on()
        {
            using var db = await SeedAsync(switchOn: true);
            (await VisibleIds(db, Admin)).Should().Equal(10, 12, 14);
            (await VisibleIds(db, Cook)).Should().Equal(12, 14);
            (await VisibleIds(db, null)).Should().Equal(12);
        }

        [Fact]
        public async Task Switch_off_hides_them_from_admins_too()
        {
            using var db = await SeedAsync(switchOn: false);
            (await VisibleIds(db, Admin)).Should().Equal(12);
        }

        [Fact]
        public async Task Source_prose_never_reaches_the_public_pool_even_when_approved()
        {
            using var db = await SeedAsync(switchOn: false);
            (await VisibleIds(db, null)).Should().NotContain(13);
            (await VisibleIds(db, Admin)).Should().NotContain(13);
            (await VisibleIds(db, Cook)).Should().NotContain(13).And.Contain(14, "authors still see their own imports");
        }
    }
}

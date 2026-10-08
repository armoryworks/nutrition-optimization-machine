using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Nom.Data;
using Nom.Data.Recipe;
using Nom.Data.Reference;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Recipe;
using Nom.Orch.Services;
using Nom.Orch.UtilityInterfaces;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// Re-importing a URL must only short-circuit to a copy the caller can open,
    /// and must say so.
    /// </summary>
    public class RecipeImportDedupTests
    {
        private const string Url = "https://www.loveandlemons.com/hummus-recipe/";

        private static ApplicationDbContext NewContext() =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        private static (RecipeScrapingService Service, Mock<IRecipeScraperClient> Scraper) NewService(ApplicationDbContext db, long personId)
        {
            var currentUser = new Mock<ICurrentUserService>();
            currentUser.SetupGet(u => u.PersonId).Returns(personId);
            var scraper = new Mock<IRecipeScraperClient>();
            scraper.SetupGet(s => s.IsConfigured).Returns(true);
            scraper.Setup(s => s.ScrapeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ScraperResult { Success = false, Error = "scraped" });
            var sources = new Mock<IScrapingSourceService>();
            sources.Setup(s => s.GetDomainStatusAsync(It.IsAny<string>())).ReturnsAsync(ScrapingSourceStatusEnum.Approved);

            var service = new RecipeScrapingService(
                db, currentUser.Object, scraper.Object, sources.Object,
                new Mock<IRecipeVettingService>().Object,
                NullLogger<RecipeScrapingService>.Instance);
            return (service, scraper);
        }

        private static async Task<long> SeedImportAsync(ApplicationDbContext db, long authorId)
        {
            if (!await db.References.AnyAsync())
            {
                db.References.Add(new ReferenceEntity { Id = (long)CurationStatusEnum.NonCurated, Name = "NonCurated" });
            }

            var recipe = new RecipeEntity { Name = "BEST Hummus", SourceUrl = Url, AuthorId = authorId, CurationStatusId = (long)CurationStatusEnum.NonCurated };
            db.Recipes.Add(recipe);
            await db.SaveChangesAsync();
            return recipe.Id;
        }

        [Fact]
        public async Task Own_earlier_import_is_returned_and_flagged()
        {
            using var db = NewContext();
            var id = await SeedImportAsync(db, authorId: 7);
            var (service, scraper) = NewService(db, personId: 7);

            var result = await service.ScrapeRecipeFromUrlAsync(new RecipeScrapingRequestModel { Url = Url });

            result.Success.Should().BeTrue();
            result.AlreadyImported.Should().BeTrue();
            result.RecipeId.Should().Be(id);
            result.Message.Should().Be("You already imported this recipe.");
            scraper.Verify(s => s.ScrapeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task Another_users_private_import_does_not_short_circuit()
        {
            using var db = NewContext();
            await SeedImportAsync(db, authorId: 23);
            var (service, scraper) = NewService(db, personId: 7);

            var result = await service.ScrapeRecipeFromUrlAsync(new RecipeScrapingRequestModel { Url = Url });

            result.AlreadyImported.Should().BeFalse();
            scraper.Verify(s => s.ScrapeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}

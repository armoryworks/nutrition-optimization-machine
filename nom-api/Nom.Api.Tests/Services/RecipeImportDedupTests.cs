using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Nom.Data;
using Nom.Data.Measurement;
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

        [Fact]
        public async Task A_food_named_on_two_lines_keeps_both_lines_each_with_its_own_amount()
        {
            using var db = NewContext();
            db.Set<BaseMeasurementEntity>().AddRange(
                new BaseMeasurementEntity { Id = 1, Name = "Gram", Symbol = "g", MeasurementCategoryId = 1, IsBaseUnit = true, BaseUnitConversionFactor = 1m },
                new BaseMeasurementEntity { Id = 3, Name = "Piece", Symbol = "pc", MeasurementCategoryId = 3, IsBaseUnit = true, BaseUnitConversionFactor = 1m },
                new BaseMeasurementEntity { Id = 11, Name = "Cup", Symbol = "cup", MeasurementCategoryId = 2, BaseUnitConversionFactor = 236.588m });
            await db.SaveChangesAsync();
            var currentUser = new Mock<ICurrentUserService>();
            currentUser.SetupGet(u => u.PersonId).Returns(7);
            var scraper = new Mock<IRecipeScraperClient>();
            scraper.SetupGet(s => s.IsConfigured).Returns(true);
            scraper.Setup(s => s.ScrapeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new ScraperResult
            {
                Success = true,
                Recipe = new ScraperRecipe
                {
                    Name = "Sugar Cookies",
                    Ingredients = new List<ScraperIngredient>
                    {
                        new() { RawLine = "1 3/4 cup granulated sugar", Name = "granulated sugar" },
                        new() { RawLine = "2 large eggs", Name = "eggs" },
                        new() { RawLine = "1/3 cup granulated sugar, (for rolling cookies)", Name = "granulated sugar" },
                        new() { RawLine = "granulated sugar, to finish", Name = "granulated sugar" },
                    },
                },
            });
            var sources = new Mock<IScrapingSourceService>();
            sources.Setup(s => s.GetDomainStatusAsync(It.IsAny<string>())).ReturnsAsync(ScrapingSourceStatusEnum.Approved);
            var vetting = new Mock<IRecipeVettingService>();
            vetting.Setup(v => v.VetAsync(It.IsAny<ScraperRecipe>())).ReturnsAsync(new List<string>());
            var service = new RecipeScrapingService(
                db, currentUser.Object, scraper.Object, sources.Object, vetting.Object, NullLogger<RecipeScrapingService>.Instance);

            var result = await service.ScrapeRecipeFromUrlAsync(new RecipeScrapingRequestModel { Url = Url });

            result.Success.Should().BeTrue();
            var sugar = await db.Ingredients.SingleAsync(i => i.Name == "granulated sugar");
            var rows = await db.RecipeIngredients.Where(ri => ri.RecipeId == result.RecipeId).OrderBy(ri => ri.SortOrder).ToListAsync();
            rows.Select(r => (r.SortOrder, r.RawLine, r.Quantity, r.MeasurementId)).Should().Equal(
                (0, "1 3/4 cup granulated sugar", 1.75m, 11L),
                (1, "2 large eggs", 2m, 3L),
                (2, "1/3 cup granulated sugar, (for rolling cookies)", 0.333m, 11L),
                (3, "granulated sugar, to finish", 0m, 1L));
            rows.Where(r => r.IngredientId == sugar.Id).Select(r => r.SortOrder).Should().Equal(0, 2, 3);
        }
    }
}

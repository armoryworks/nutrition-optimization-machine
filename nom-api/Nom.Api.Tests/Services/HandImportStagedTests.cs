using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Nom.Data;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Recipe;
using Nom.Orch.Services;
using Nom.Orch.UtilityInterfaces;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// Hand imports (ops/hand-import.py): recipes read from the pages of a PDF come in through the
    /// staged import under their hand source, each with the document's URL and its page as the
    /// source link, so the recipes of one document are not taken for duplicates of each other.
    /// </summary>
    public class HandImportStagedTests
    {
        private const long Admin = 7;
        private const string Pdf = "https://www.cancer.gov/about-cancer/causes-prevention/risk/diet/down-home-healthy-cooking.pdf";

        private static ApplicationDbContext NewContext() =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options);

        [Theory]
        [InlineData("https://www.cancer.gov/a/book.pdf#page=20", "https://www.cancer.gov/a/book.pdf#page=20")]
        [InlineData("https://WWW.Cancer.gov/a/book.pdf#page=20&search=%22Succotash%22", "https://www.cancer.gov/a/book.pdf#page=20&search=%22Succotash%22")]
        [InlineData("https://blog.example/stew/#comments", "https://blog.example/stew/")]
        [InlineData("https://blog.example/stew/#pages", "https://blog.example/stew/")]
        [InlineData("https://www.loc.gov/resource/gdcmassbookdig.homecookbooktrie00unse/?sp=50&st=text", "https://www.loc.gov/resource/gdcmassbookdig.homecookbooktrie00unse/?sp=50&st=text")]
        public void A_source_url_keeps_only_a_fragment_that_opens_a_page_of_a_document(string input, string expected)
        {
            RecipeScrapingService.NormalizeUrl(input).Should().Be(expected);
        }

        [Fact]
        public async Task The_recipes_of_one_document_come_in_by_page_under_their_hand_source_and_a_rerun_takes_none_again()
        {
            using var db = NewContext();
            var hand = new ScrapingSourceEntity
            {
                Domain = "www.cancer.gov",
                Status = ScrapingSourceStatusEnum.Approved,
                Kind = ScrapingSourceEntity.HandKind,
                LicenceKind = "public-domain",
                CreatedDate = DateTime.UtcNow,
            };
            db.ScrapingSources.Add(hand);
            await db.SaveChangesAsync();
            var import = Staged(db);
            var request = new StagedImportRequestModel
            {
                SourceId = hand.Id,
                SourceAttribution = "Adapted from National Cancer Institute, Down Home Healthy Cooking (NIH Publication No. 06-3408)",
                Recipes = { Record("Hot 'n Spicy Seasoning", Pdf + "#page=20"), Record("Succotash", Pdf + "#page=25"), Record("New Orleans Red Beans", Pdf + "#page=26") },
            };

            var first = await import.ImportStagedAsync(request);

            first.Imported.Should().Be(3);
            first.Refused.Should().Be(0);
            var recipes = await db.Recipes.OrderBy(r => r.Id).ToListAsync();
            recipes.Select(r => r.SourceUrl).Should().Equal(Pdf + "#page=20", Pdf + "#page=25", Pdf + "#page=26");
            recipes.Should().OnlyContain(r => r.LicenseStatus == RecipeLicenseStatus.PublicDomain && r.SourceAttribution == request.SourceAttribution);
            var documents = await db.ScrapedDocuments.OrderBy(d => d.RecipeId).ToListAsync();
            documents.Select(d => d.Format).Should().OnlyContain(f => f == ScrapedDocumentFormat.ScraperRecipe);
            documents.Select(d => d.SourceUrl).Should().Equal(recipes.Select(r => r.SourceUrl));
            documents.Should().OnlyContain(d => d.RawJsonLd.Contains("#page=") && d.ContentSha256 != null);
            (await SourceAdmissionService.RecipesFrom(db.Recipes, hand.Domain).CountAsync()).Should().Be(3);

            var again = await Staged(db).ImportStagedAsync(request);

            again.Imported.Should().Be(0);
            again.SkippedDuplicates.Should().Be(3);
            (await db.Recipes.CountAsync()).Should().Be(3);
        }

        private static ScraperRecipe Record(string name, string url) => new()
        {
            Name = name,
            SourceUrl = url,
            SourceSite = "National Cancer Institute",
            Ingredients = { new ScraperIngredient { RawLine = "1 teaspoon garlic powder" }, new ScraperIngredient { RawLine = "1/2 cup chopped onions" } },
            Steps = { new ScraperStep { Order = 1, Instruction = "Mix together all ingredients." } },
        };

        private static RecipeScrapingService Staged(ApplicationDbContext db)
        {
            var user = new Mock<ICurrentUserService>();
            user.SetupGet(u => u.PersonId).Returns(Admin);
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { [RecipeScrapingService.StagedImportGateSetting] = "true" })
                .Build();
            var vetting = new Mock<IRecipeVettingService>();
            vetting.Setup(v => v.VetAsync(It.IsAny<ScraperRecipe>())).ReturnsAsync(new List<string>());
            return new RecipeScrapingService(db, user.Object, new Mock<IRecipeScraperClient>().Object, new Mock<IScrapingSourceService>().Object,
                vetting.Object, NullLogger<RecipeScrapingService>.Instance, configuration);
        }
    }
}

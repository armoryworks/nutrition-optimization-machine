using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Nom.Data;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// The stock-phrase index is built from the stored source documents: a phrase is stock when
    /// three or more sites use it, a site counts once however many of its documents use it, a dry
    /// run writes nothing, a write adds the index under the next version, keeps the earlier ones
    /// and is journaled, an unchanged index is not written again, and an earlier version can be
    /// loaded and restored.
    /// </summary>
    public class ProseStockServiceTests
    {
        private static readonly DateTime Fetched = new(2026, 11, 3, 0, 0, 0, DateTimeKind.Utc);

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

        private static string Document(string description, params string[] steps) =>
            "{\"@type\":\"Recipe\",\"description\":\"" + description + "\",\"recipeInstructions\":["
            + string.Join(",", steps.Select(s => "{\"@type\":\"HowToStep\",\"text\":\"" + s + "\"}")) + "]}";

        private static async Task AddAsync(ApplicationDbContext db, string url, string rawJsonLd)
        {
            db.ScrapedDocuments.Add(new ScrapedDocumentEntity { RecipeId = 1, SourceUrl = url, RawJsonLd = rawJsonLd, FetchedAtUtc = Fetched, CreatedDate = Fetched });
            await db.SaveChangesAsync();
        }

        private static async Task<ApplicationDbContext> SeedAsync()
        {
            var db = NewContext();
            await AddAsync(db, "https://www.alpha.example/stew", Document("A stew.", "Preheat the oven to 350 degrees and bring a large pot of water to a boil.", "Braise the beef slowly."));
            await AddAsync(db, "https://alpha.example/soup", Document("A soup.", "Preheat the oven to 400 degrees and bring a large pot of water to a boil."));
            await AddAsync(db, "https://beta.example/cake", Document("A cake.", "Preheat the oven to 325 degrees.", "Bring a large pot of water to a boil."));
            await AddAsync(db, "https://GAMMA.example/bread", Document("A loaf.", "Preheat the oven to 450 degrees and knead the dough well."));
            await AddAsync(db, "https://gamma.example/broken", "{\"@type\":\"Recipe\"}");
            await AddAsync(db, "not an address", Document("None.", "Preheat the oven to 350 degrees."));
            return db;
        }

        [Fact]
        public async Task A_phrase_of_three_sites_is_stock_and_one_of_two_sites_is_not()
        {
            await using var db = await SeedAsync();
            var service = new ProseStockService(db, new NoUser());

            var result = await service.RebuildAsync(apply: true);
            var index = await service.LoadAsync();

            result.Documents.Should().Be(6);
            result.DocumentsUnread.Should().Be(1);
            result.DocumentsWithoutSite.Should().Be(1);
            result.Sites.Should().Be(3);
            result.DocumentsBySite.Should().BeEquivalentTo(new System.Collections.Generic.Dictionary<string, int>
            {
                ["alpha.example"] = 2, ["beta.example"] = 1, ["gamma.example"] = 1,
            });
            index.Version.Should().Be(1);
            index.IsStock(ProsePhrases.Hash("preheat the oven to 350")).Should().BeTrue("alpha, beta and gamma all use it, numbers read as #");
            index.IsStock(ProsePhrases.Hash("bring a large pot of")).Should().BeFalse("only alpha and beta use it, and alpha's two documents count once");
            index.IsStock(ProsePhrases.Hash("braise the beef slowly a")).Should().BeFalse();
            result.StockPhrases.Should().Be(index.Count);
        }

        [Fact]
        public async Task A_dry_run_writes_nothing()
        {
            await using var db = await SeedAsync();
            var service = new ProseStockService(db, new NoUser());

            var result = await service.RebuildAsync();

            result.Applied.Should().BeFalse();
            result.Version.Should().Be(1);
            result.Added.Should().Be(result.StockPhrases);
            (await db.ProseStockPhrases.CountAsync()).Should().Be(0);
            (await db.AuditLogEntries.CountAsync()).Should().Be(0);
            (await service.LoadAsync()).Should().BeSameAs(ProseStockIndex.Empty);
        }

        [Fact]
        public async Task A_write_adds_the_index_under_the_next_version_and_an_unchanged_one_is_not_written()
        {
            await using var db = await SeedAsync();
            var service = new ProseStockService(db, new NoUser());
            await service.RebuildAsync(apply: true);

            var again = await service.RebuildAsync(apply: true);
            again.Version.Should().Be(1);
            again.Added.Should().Be(0);
            again.Removed.Should().Be(0);
            (await db.AuditLogEntries.CountAsync()).Should().Be(1);

            await AddAsync(db, "https://delta.example/pasta", Document("Pasta.", "Bring a large pot of water to a boil."));
            var next = await service.RebuildAsync(apply: true);
            var index = await service.LoadAsync();

            next.CurrentVersion.Should().Be(1);
            next.Version.Should().Be(2);
            next.Added.Should().BeGreaterThan(0);
            index.Version.Should().Be(2);
            index.IsStock(ProsePhrases.Hash("bring a large pot of")).Should().BeTrue("delta is its third site");
            (await db.ProseStockPhrases.Select(p => p.Version).Distinct().OrderBy(v => v).ToListAsync()).Should().Equal(1, 2);

            var journal = await db.AuditLogEntries.OrderBy(a => a.Id).ToListAsync();
            journal.Should().HaveCount(2);
            journal.Should().OnlyContain(a => a.EntityType == ProseStockService.EntityType && a.ChangeType == ProseStockService.RebuildChangeType);
            journal[1].EntityId.Should().Be(2);
            journal[1].OldValue.Should().StartWith("version 1:");
        }

        [Fact]
        public async Task An_earlier_version_survives_a_rebuild_and_can_be_restored_as_the_next_version()
        {
            await using var db = await SeedAsync();
            var service = new ProseStockService(db, new NoUser());
            await service.RebuildAsync(apply: true);
            var first = await service.LoadAsync();
            await AddAsync(db, "https://delta.example/pasta", Document("Pasta.", "Bring a large pot of water to a boil."));
            await service.RebuildAsync(apply: true);

            var kept = await service.LoadAsync(1);
            kept.Version.Should().Be(1);
            kept.Count.Should().Be(first.Count);
            kept.IsStock(ProsePhrases.Hash("bring a large pot of")).Should().BeFalse();

            var dryRun = await service.RestoreAsync(1);
            dryRun.Found.Should().BeTrue();
            dryRun.Applied.Should().BeFalse();
            dryRun.Version.Should().Be(3);
            dryRun.Removed.Should().BeGreaterThan(0);
            (await service.LoadAsync()).Version.Should().Be(2);

            var restored = await service.RestoreAsync(1, apply: true);
            var index = await service.LoadAsync();
            restored.Applied.Should().BeTrue();
            index.Version.Should().Be(3);
            index.Count.Should().Be(first.Count);
            index.IsStock(ProsePhrases.Hash("bring a large pot of")).Should().BeFalse();
            (await service.LoadAsync(2)).IsStock(ProsePhrases.Hash("bring a large pot of")).Should().BeTrue();

            var journal = await db.AuditLogEntries.OrderBy(a => a.Id).ToListAsync();
            journal.Should().HaveCount(3);
            journal[2].ChangeType.Should().Be(ProseStockService.RestoreChangeType);
            journal[2].EntityId.Should().Be(3);
            journal[2].NewValue.Should().Contain("of version 1");

            (await service.RestoreAsync(3, apply: true)).Applied.Should().BeFalse("version 3 is the current index");
            (await service.RestoreAsync(9, apply: true)).Found.Should().BeFalse();
            (await db.AuditLogEntries.CountAsync()).Should().Be(3);
        }

        [Theory]
        [InlineData("https://www.Budget.example/recipe/1", "budget.example")]
        [InlineData("http://cooking.example", "cooking.example")]
        [InlineData("not an address", null)]
        [InlineData("", null)]
        public void The_site_of_a_source_is_its_host_without_www(string url, string? site)
        {
            ProseStockService.Site(url).Should().Be(site);
        }
    }
}

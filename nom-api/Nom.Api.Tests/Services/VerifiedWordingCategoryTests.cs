using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Nom.Data;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Curation;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// The category verdict of the verified wording table (batch w02): an old cookbook's food word that no single catalog
    /// food is ("isinglass" is fish gelatin, "calf's head" a veal variety meat) is given the USDA category and the leading
    /// part of the description its foods share, refused when no live curated food is of that group, journaled with its
    /// group, and put back by an unload to the group it replaced.
    /// </summary>
    public class VerifiedWordingCategoryTests
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

        private static IngredientEntity Curated(long id, string name, int category, string description) => new()
        {
            Id = id,
            Name = name,
            FdcCategoryId = category,
            CurationStatusId = (long)CurationStatusEnum.Curated,
            Aliases = new List<IngredientAliasEntity> { new() { AliasName = description, SourceContext = IngredientLinkService.UsdaDescriptionContext } },
        };

        private static async Task SeedAsync(ApplicationDbContext db)
        {
            db.Ingredients.AddRange(
                Curated(1, "Cod", 15, "Fish, cod, Atlantic, raw"),
                Curated(2, "Shrimp", 15, "Crustaceans, shrimp, raw"),
                Curated(3, "Veal Brain", 17, "Veal, variety meats and by-products, brain, raw"),
                Curated(4, "Fast foods, hamburger", 21, "Fast foods, hamburger"));
            await db.SaveChangesAsync();
        }

        [Fact]
        public void The_batch_file_gives_a_category_row_its_category_and_head()
        {
            var problems = new List<VerifiedWordingProblem>();

            var rows = VerifiedWordingService.ParseTsv(
                "wording\tverdict\tfood_id\tfood_fdc_id\tfood_name\tcategory_id\tcategory_head\tlines_at_review\tbatch_row\tstatus\tbasis\treason\n"
                + "isinglass\tcategory\t\t\t\t15\tFish\t296\tW02-1\tagent\tWebster 1913\tfish gelatin\n", problems);

            problems.Should().BeEmpty();
            rows.Should().ContainSingle().Which.Should().Match<VerifiedWordingRow>(r =>
                r.Verdict == "category" && r.CategoryId == 15 && r.CategoryHead == "Fish" && r.Row == "W02-1" && r.Note == "fish gelatin");
        }

        [Fact]
        public async Task A_category_row_is_written_with_its_group_and_refused_without_a_curated_food_of_it()
        {
            using var db = NewContext();
            await SeedAsync(db);
            var service = new VerifiedWordingService(db, new NoUser());
            var rows = new[]
            {
                new VerifiedWordingRow { Row = "1", Wording = "isinglass", Verdict = "category", CategoryId = 15, CategoryHead = " Fish " },
                new VerifiedWordingRow { Row = "2", Wording = "calf's head", Verdict = "category", CategoryId = 17, CategoryHead = "Veal, variety meats and by-products" },
                new VerifiedWordingRow { Row = "3", Wording = "sturgeon", Verdict = "category", CategoryId = 15, CategoryHead = "Fis" },
                new VerifiedWordingRow { Row = "4", Wording = "partridges", Verdict = "category" },
                new VerifiedWordingRow { Row = "5", Wording = "tournedos", Verdict = "category", CategoryId = 21 },
            };

            var result = await service.LoadAsync(rows, "w02", apply: true);

            result.Should().Match<VerifiedWordingLoadResult>(r => r.Added == 2 && r.Rejected == 3);
            result.Problems.Select(p => (p.Wording, p.Reason)).Should().BeEquivalentTo(new[]
            {
                ("sturgeon", VerifiedWordingService.CategoryMissing),
                ("partridges", VerifiedWordingService.CategoryMissing),
                ("tournedos", VerifiedWordingService.CategoryPrepared),
            }, "a head matches the description up to a comma, a category row needs its category, and a prepared category is never a wording's");
            var isinglass = await db.IngredientWordings.SingleAsync(w => w.Wording == "isinglass");
            isinglass.Should().Match<IngredientWordingEntity>(w => w.Verdict == "category" && w.IngredientId == null && w.FdcCategoryId == 15 && w.CategoryHead == "Fish");
            (await db.AuditLogEntries.Where(a => a.EntityId == isinglass.Id).Select(a => a.PropertyName + "=" + a.NewValue).ToListAsync())
                .Should().BeEquivalentTo("IngredientId=category:", "FdcCategoryId=15|Fish");

            (await service.LoadAsync(new[] { new VerifiedWordingRow { Row = "1", Wording = "isinglass", Verdict = "category", CategoryId = 15, CategoryHead = "fish" } }, "w02"))
                .Unchanged.Should().Be(1, "a head is compared without case");
            (await service.ExportTsvAsync()).Should().Contain("isinglass\tcategory\t\t\t\tw02\t\t15|Fish");
        }

        [Fact]
        public async Task An_unload_puts_back_the_group_a_later_batch_replaced_and_a_retract_remembers_it()
        {
            using var db = NewContext();
            await SeedAsync(db);
            var service = new VerifiedWordingService(db, new NoUser());
            await service.LoadAsync(new[] { new VerifiedWordingRow { Row = "1", Wording = "isinglass", Verdict = "category", CategoryId = 15 } }, "w02", apply: true);
            await service.LoadAsync(new[] { new VerifiedWordingRow { Row = "1", Wording = "isinglass", Verdict = "category", CategoryId = 15, CategoryHead = "Fish" } }, "w03", apply: true, replace: true);

            (await service.UnloadAsync("w03", apply: true)).Restored.Should().Be(1);

            var row = await db.IngredientWordings.SingleAsync(w => w.Wording == "isinglass");
            row.Should().Match<IngredientWordingEntity>(w => w.Batch == "w02" && w.FdcCategoryId == 15 && w.CategoryHead == null);

            await service.RetractAsync(row.Id, "a reviewer reads it as fish only", apply: true);
            (await db.IngredientWordingRejections.SingleAsync()).Should().Match<IngredientWordingRejectionEntity>(r => r.FdcCategoryId == 15 && r.CategoryHead == null);
            var again = await service.LoadAsync(new[]
            {
                new VerifiedWordingRow { Row = "1", Wording = "isinglass", Verdict = "category", CategoryId = 15 },
            }, "w04");
            again.Problems.Should().ContainSingle(p => p.Reason == VerifiedWordingService.RetractedBefore);
            (await service.LoadAsync(new[] { new VerifiedWordingRow { Row = "1", Wording = "isinglass", Verdict = "category", CategoryId = 15, CategoryHead = "Fish" } }, "w04"))
                .Added.Should().Be(1, "another group is another answer");
        }

        [Fact]
        public void Batch_w02_is_read_whole_and_holds_only_cited_agent_rows_of_food_or_category_with_no_ambiguous_bare_word()
        {
            var text = System.IO.File.ReadAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", "VerifiedWordings", "batch-w02.tsv"));
            var problems = new List<VerifiedWordingProblem>();

            var rows = VerifiedWordingService.ParseTsv(text, problems);

            problems.Should().BeEmpty();
            rows.Should().HaveCountGreaterThan(150);
            rows.Select(r => r.Wording).Should().OnlyHaveUniqueItems();
            rows.Should().OnlyContain(r => IngredientNameNormalizer.WordingKey(r.Wording) == r.Wording, "the loader refuses a wording that is not in key form");
            rows.Should().OnlyContain(r => (r.Verdict == "food" && r.FoodFdcId != null && r.CategoryId == null)
                || (r.Verdict == "category" && r.CategoryId != null && r.FoodFdcId == null));
            rows.Select(r => r.Wording).Should().NotContain(new[] { "stock", "dripping", "jelly", "soup", "forcemeat", "white stock", "consomme", "marrow", "two cloves" },
                "a wording whose meaning the cookbooks leave open stays unknown");
            rows.Select(r => r.Wording).Should().NotContain(w => System.Text.RegularExpressions.Regex.IsMatch(w, @"\b(consomm[eé]s?|fumets?)\b"),
                "a qualifier settles a consommé's meat or a fumet's fish but not the egg white that clarifies the one or the wine in the other");
            var lines = text.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var header = lines[0].Split('\t').ToList();
            lines.Skip(1).Select(l => l.Split('\t')).Should().OnlyContain(c => c[header.IndexOf("status")] == "agent" && c[header.IndexOf("basis")].Length > 20,
                "every row is judged by an agent, twice, and cites a public-domain definition or USDA's own description");
        }

        [Theory]
        [InlineData(15, "Fish", 15, "Fish, cod, Atlantic, raw", true)]
        [InlineData(15, "Fish", 15, "Fish", true)]
        [InlineData(15, "Fish", 15, "Fishes, mixed", false)]
        [InlineData(15, "fish, cod", 15, "Fish, cod, Pacific, raw", true)]
        [InlineData(15, null, 15, "Crustaceans, shrimp, raw", true)]
        [InlineData(15, "Fish", 17, "Fish, cod, Atlantic, raw", false)]
        public void A_group_holds_the_foods_of_its_category_whose_description_starts_with_its_head(int category, string? head, int foodCategory, string description, bool holds) =>
            WordingCategory.Holds(category, WordingCategory.Head(head), foodCategory, description, "name").Should().Be(holds);
    }
}

using System;
using System.Linq;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Nom.Data;
using Nom.Data.Nutrition;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;
using Xunit;
using Xunit.Abstractions;

namespace Nom.Api.Tests.Services
{
    /// <summary>The link re-check's line and pick queries translate to Postgres SQL with no client evaluation.</summary>
    public class LinkRecheckPostgresTranslationTests
    {
        private readonly ITestOutputHelper _output;

        public LinkRecheckPostgresTranslationTests(ITestOutputHelper output) => _output = output;

        private static ApplicationDbContext Postgres() =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql("Host=localhost;Database=none").Options,
                new HttpContextAccessor { HttpContext = new DefaultHttpContext() });

        private string Sql<T>(string name, IQueryable<T> query)
        {
            var sql = query.ToQueryString();
            _output.WriteLine($"-- {name}\n{sql};\n");
            return sql;
        }

        [Fact]
        public void The_alcohol_line_query_looks_for_every_stem_in_the_database()
        {
            using var db = Postgres();
            var sql = Sql("alcohol lines", LinkRecheckService.ScrapedLines(db, curatedOnly: true).Where(LinkRecheckService.ContainsAny(LinkRecheckRules.AlcoholStems))
                .Select(ri => new { ri.RecipeId, ri.IngredientId, ri.SortOrder, ri.RawLine }));
            sql.Should().Contain("lower(").And.Contain("LIKE").And.Contain("\"ScrapedAtUtc\" IS NOT NULL").And.Contain("\"CurationStatusId\"");
        }

        [Fact]
        public void The_prepared_line_query_reads_the_stored_category()
        {
            using var db = Postgres();
            var categories = CatalogAdmissionRule.PreparedCategories.Keys.ToList();
            var sql = Sql("prepared lines", LinkRecheckService.ScrapedLines(db, curatedOnly: false)
                .Where(ri => ri.Ingredient.FdcCategoryId != null && categories.Contains(ri.Ingredient.FdcCategoryId.Value))
                .Select(ri => new { ri.RecipeId, ri.IngredientId, ri.SortOrder, ri.RawLine }));
            sql.Should().Contain("\"FdcCategoryId\"");
        }

        [Fact]
        public void The_waiting_pick_query_runs_in_the_database()
        {
            using var db = Postgres();
            var sql = Sql("waiting picks", db.FoodCatalogProposals.Where(LinkRecheckService.WaitingModelPick).Select(p => new { p.Id, p.IngredientId, p.Source }));
            sql.Should().Contain("LIKE 'ai:%'").And.Contain("\"ReviewedByPersonId\"");
        }
    }
}

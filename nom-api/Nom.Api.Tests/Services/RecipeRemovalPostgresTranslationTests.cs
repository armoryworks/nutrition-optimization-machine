using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Nom.Data;
using Nom.Orch.Services;
using Xunit;
using Xunit.Abstractions;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// The removal's queries that select what it retires, which journal rows a purge reduces, and the refusal of a removed
    /// address translate to Postgres SQL with no client evaluation. The SQL is written to the test output so it can be
    /// explained against a staging copy.
    /// </summary>
    public class RecipeRemovalPostgresTranslationTests
    {
        private readonly ITestOutputHelper _output;

        public RecipeRemovalPostgresTranslationTests(ITestOutputHelper output) => _output = output;

        private static ApplicationDbContext Postgres() =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql("Host=localhost;Database=none").Options);

        [Fact]
        public void The_candidates_the_journal_rows_and_the_address_refusal_translate()
        {
            using var db = Postgres();
            var candidates = RecipeRemovalService.CandidatesOf(db, "www.forbidden.example").Select(r => new { r.Id, r.IsDeleted, r.CurationStatusId }).ToQueryString();
            var audit = RecipeRemovalService.AuditRowsOf(db, new long[] { 1, 2 }, new List<long> { 3 }, new List<long> { 4 }).ToQueryString();
            var key = RecipeRemovalService.UrlKey("https://www.forbidden.example/stew/");
            var refusal = db.RecipeRemovals.AsNoTracking().Where(r => r.UrlKey == key && r.UndoneAtUtc == null).OrderBy(r => r.Id).ToQueryString();
            _output.WriteLine(candidates);
            _output.WriteLine(audit);
            _output.WriteLine(refusal);

            candidates.Should().Contain("LIKE").And.Contain("NOT EXISTS").And.Contain("\"RecipeRemoval\"").And.NotContain("\"IsDeleted\" = FALSE", "deleted recipes of the source are removed too");
            audit.Should().Contain("\"AuditLogEntry\"").And.Contain("\"EntityType\"");
            refusal.Should().Contain("\"UrlKey\"");
            key.Should().Be("forbidden.example/stew");
        }

        [Fact]
        public void The_wording_queries_of_the_retire_its_undo_and_the_purge_translate()
        {
            using var db = Postgres();
            var retired = RecipeRemovalService.RetiredFoodsOf(db, new List<long> { 1, 2 }).ToQueryString();
            var inUse = RecipeRemovalService.TextsInUseOf(db, new List<long> { 3 }, new List<string> { "1 cup brandy" }).ToQueryString();
            var worded = RecipeRemovalService.ProposalsWordedOf(db, new List<string> { "1 cup brandy" }).ToQueryString();
            var merged = RecipeRemovalService.MergedAwayFoodsOf(db, new List<string> { "1 cup brandy" }).ToQueryString();
            var aliases = RecipeRemovalService.RetiredAliasesOf(db, new List<string> { "retired:1" }).Select(a => a.AliasName).ToQueryString();
            _output.WriteLine(retired);
            _output.WriteLine(merged);
            _output.WriteLine(aliases);
            _output.WriteLine(inUse);
            _output.WriteLine(worded);

            retired.Should().Contain("\"AuditLogEntry\"").And.Contain("EXISTS");
            inUse.Should().Contain("lower(").And.Contain("btrim(");
            worded.Should().Contain("\"FoodCatalogProposal\"").And.Contain("lower(");
            merged.Should().Contain("strpos(").And.Contain("substring(").And.Contain("btrim(");
            aliases.Should().Contain("\"SourceContext\"");
        }
    }
}

using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Nom.Data;
using Nom.Orch.Services;
using Xunit;
using Xunit.Abstractions;

namespace Nom.Api.Tests.Services
{
    /// <summary>The wording reader lane's selection queries (the tiers' decisions, its own decisions, the sole-blocked recipes) and the lexicon read translate to Postgres SQL with no client evaluation.</summary>
    public class WordingReaderPostgresTranslationTests
    {
        private readonly ITestOutputHelper _output;

        public WordingReaderPostgresTranslationTests(ITestOutputHelper output)
        {
            _output = output;
        }

        private sealed class CaptureAndStop : DbCommandInterceptor
        {
            public string? Sql { get; set; }

            public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
                DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
            {
                Sql = command.CommandText;
                throw new OperationCanceledException("captured");
            }
        }

        private sealed class NoConnection : DbConnectionInterceptor
        {
            public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
                DbConnection connection, ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default) =>
                ValueTask.FromResult(InterceptionResult.Suppress());
        }

        private async Task<string> Capture(string name, Func<ApplicationDbContext, Task> run)
        {
            var capture = new CaptureAndStop();
            await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                    .UseNpgsql("Host=localhost;Database=none")
                    .AddInterceptors(capture, new NoConnection())
                    .Options,
                new HttpContextAccessor { HttpContext = new DefaultHttpContext() });
            var act = () => run(db);
            await act.Should().ThrowAsync<OperationCanceledException>();
            capture.Sql.Should().NotBeNullOrEmpty();
            _output.WriteLine($"-- {name}\n{capture.Sql};\n");
            return capture.Sql!;
        }

        [Fact]
        public async Task Every_selection_query_of_the_lane_translates()
        {
            var tiers = await Capture("tier decisions", db => WordingReaderLaneService.TierDecisionQuery(db).ToListAsync());
            var decided = await Capture("decided", db => WordingReaderLaneService.DecidedQuery(db).ToListAsync());
            var blockers = await Capture("blockers", db => WordingReaderLaneService.BlockersQuery(db, new List<long> { 1, 2 }).ToListAsync());
            var lexicon = await Capture("lexicon", db => db.WordingLexicon.Select(l => new { l.Term, l.Means }).ToListAsync());

            tiers.Should().ContainEquivalentOf("distinct").And.ContainEquivalentOf("like");
            decided.Should().ContainEquivalentOf("like");
            blockers.Should().ContainEquivalentOf("not exists").And.ContainEquivalentOf("count(distinct");
            lexicon.Should().Contain("\"WordingLexicon\"");
        }
    }
}

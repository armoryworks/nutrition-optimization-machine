using System;
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
    /// <summary>The wording tier lane's selection, decision, cap, recovery and curated-line queries translate to Postgres SQL with no client evaluation.</summary>
    public class WordingTierPostgresTranslationTests
    {
        private readonly ITestOutputHelper _output;

        public WordingTierPostgresTranslationTests(ITestOutputHelper output)
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
            var waiting = await Capture("waiting", db => WordingTierLaneService.WaitingQuery(db).ToListAsync());
            var decided = await Capture("decided", db => WordingTierLaneService.DecidedQuery(db).ToListAsync());
            var curated = await Capture("curated lines", db => WordingTierLaneService.CuratedLinesQuery(db).ToListAsync());
            var capped = await Capture("capped", db => WordingTierLaneService.CappedQuery(db, DateTime.UtcNow).ToListAsync());
            var orphans = await Capture("orphans", db => WordingTierLaneService.OrphanQuery(db, DateTime.UtcNow).ToListAsync());

            waiting.Should().Contain("lower(").And.ContainEquivalentOf("count(");
            decided.Should().ContainEquivalentOf("distinct");
            curated.Should().ContainEquivalentOf("case").And.ContainEquivalentOf("length(");
            capped.Should().ContainEquivalentOf("distinct").And.ContainEquivalentOf("like");
            orphans.Should().ContainEquivalentOf("not exists").And.ContainEquivalentOf("coalesce(");
        }
    }
}

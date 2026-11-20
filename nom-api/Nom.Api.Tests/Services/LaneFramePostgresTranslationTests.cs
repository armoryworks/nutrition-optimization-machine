using System;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Nom.Data;
using Nom.Orch.Interfaces;
using Nom.Orch.Services;
using Xunit;
using Xunit.Abstractions;

namespace Nom.Api.Tests.Services
{
    /// <summary>The lane frame's queries translate to Postgres SQL with no client evaluation.</summary>
    public class LaneFramePostgresTranslationTests
    {
        private readonly ITestOutputHelper _output;

        public LaneFramePostgresTranslationTests(ITestOutputHelper output)
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
        public async Task Every_frame_query_translates()
        {
            var since = new DateTime(2026, 11, 20, 0, 0, 0, DateTimeKind.Utc);

            var totals = await Capture("totals", db => LaneFrameService.TotalsQuery(db, "lane", "t1", "sig", since).FirstOrDefaultAsync());
            var window = await Capture("window", db => LaneFrameService.WindowQuery(db, "lane", "t1", "sig", since, 200).ToListAsync());
            await Capture("audit-pool", db => LaneFrameService.AuditPoolQuery(db, "lane", "t1", since).ToListAsync());
            await Capture("recent", db => LaneFrameService.RecentCountsQuery(db, since).ToListAsync());
            await Capture("mode", db => new LaneFrameService(db, Array.Empty<ILaneUndo>(), NullLogger<LaneFrameService>.Instance).ModeAsync("lane", "t1"));

            totals.Should().Contain("FILTER (WHERE").And.Contain("curation.\"LaneDecision\"");
            window.Should().Contain("LIMIT");
        }

        [Fact]
        public void The_link_gate_food_facts_query_translates_to_one_postgres_statement()
        {
            using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql("Host=localhost;Database=none").Options,
                new HttpContextAccessor { HttpContext = new DefaultHttpContext() });

            var sql = LinkAutoApplyService.GateFoodRows(db.Ingredients.Where(i => !i.IsDeleted), new long[] { 1, 2, 3, 4 }).ToQueryString();

            _output.WriteLine(sql);
            sql.Should().Contain("SELECT DISTINCT").And.Contain("\"AdmissionReason\"").And.Contain("\"FdcCategoryId\"");
        }
    }
}

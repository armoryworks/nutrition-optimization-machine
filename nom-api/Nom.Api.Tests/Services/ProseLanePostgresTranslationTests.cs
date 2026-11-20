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
    /// <summary>The prose lane's selection and the Pipeline page's give-up queries translate to Postgres SQL with no client evaluation.</summary>
    public class ProseLanePostgresTranslationTests
    {
        private readonly ITestOutputHelper _output;

        public ProseLanePostgresTranslationTests(ITestOutputHelper output) => _output = output;

        private sealed class CaptureAndStop : DbCommandInterceptor
        {
            public string? Sql { get; set; }

            public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
                DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
            {
                Sql = command.CommandText;
                throw new OperationCanceledException("captured");
            }

            public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
                DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
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
        public async Task Every_prose_lane_query_translates()
        {
            var eligible = await Capture("eligible", db => ProseLaneService.EligibleQuery(db, "v2-abc")
                .OrderBy(r => r.ProseWithheld ? 0 : 2).ThenBy(r => r.ProseRewriteAttemptedAt).Select(r => r.Id).Take(25).ToListAsync());
            await Capture("gave-up", db => ProseLaneService.GaveUpQuery(db, "v2-abc").CountAsync());
            var byClass = await Capture("by-class", db => PipelineStatusService.ProseGaveUpByClassQuery(db, "v2-abc").ToListAsync());

            eligible.Should().Contain("EXISTS").And.Contain("\"ProseLaneVersion\"");
            byClass.Should().Contain("GROUP BY").And.Contain("LIKE");
        }
    }
}

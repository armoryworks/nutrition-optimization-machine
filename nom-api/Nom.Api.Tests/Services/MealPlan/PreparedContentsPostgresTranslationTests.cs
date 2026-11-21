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
using Nom.Orch.Services.Support;
using Xunit;
using Xunit.Abstractions;

namespace Nom.Api.Tests.Services.MealPlan
{
    /// <summary>
    /// The prepared-contents queries (the shuffle's pool of meals with read contents, the gate's statements, a pass's
    /// current rows and the Pipeline page's count) translate to Postgres SQL with no client evaluation. Each command is captured
    /// before it would reach a server and written to the test output.
    /// </summary>
    public class PreparedContentsPostgresTranslationTests
    {
        private readonly ITestOutputHelper _output;

        public PreparedContentsPostgresTranslationTests(ITestOutputHelper output) => _output = output;

        private sealed class CaptureAndStop : DbCommandInterceptor
        {
            public List<string> Sql { get; } = new();

            public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
                DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
            {
                Sql.Add(command.CommandText);
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
            capture.Sql.Should().NotBeEmpty();
            _output.WriteLine($"-- {name}\n{capture.Sql[0]};\n");
            return capture.Sql[0];
        }

        [Fact]
        public async Task The_pool_the_gate_a_pass_and_the_pipeline_count_translate()
        {
            var pool = await Capture("pool-with-contents", db => MealPlanOrchestrationService.PreparedMealPoolQuery(db, 5, new List<long> { 1, 2 }, true).Select(i => i.Id).ToListAsync());
            pool.Should().Contain("\"IngredientContents\"").And.Contain("EXISTS");
            await Capture("pool", db => MealPlanOrchestrationService.PreparedMealPoolQuery(db, 5, new List<long>(), false).OrderBy(i => EF.Functions.Random()).Take(9).Select(i => i.Id).ToListAsync());
            await Capture("gate", db => PreparedContentsGate.JudgeAsync(db, 5, new RestrictedIngredientSet(), new List<long> { 10, 11 }));
            await Capture("pass", db => new PreparedContentsService(db).RecordAsync(
                new[] { new ContentsStatement(10, "fdc:1", "Ingredients: Rice, chicken, salt.") }, "translation", apply: false));
            await Capture("pipeline", db => PipelineStatusService.PreparedContentsQuery(db).ToListAsync());
        }
    }
}

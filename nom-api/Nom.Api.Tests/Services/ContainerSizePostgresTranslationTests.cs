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
    /// <summary>The container size lane's hold-out and Pipeline queries translate to Postgres SQL with no client evaluation.</summary>
    public class ContainerSizePostgresTranslationTests
    {
        private readonly ITestOutputHelper _output;

        public ContainerSizePostgresTranslationTests(ITestOutputHelper output) => _output = output;

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
        public async Task Every_container_size_query_translates()
        {
            var holdout = await Capture("holdout", db => ContainerSizeService.HoldoutQuery(db, new long[] { 1, 2 }).ToListAsync());
            var status = await Capture("status", db => ContainerSizeService.StatusQuery(db).ToListAsync());

            holdout.Should().Contain("\"MeasurementCategoryId\"").And.Contain("\"SourceAttribution\"");
            status.Should().Contain("\"IngredientContainerSize\"").And.Contain("\"Name\"");
        }
    }
}

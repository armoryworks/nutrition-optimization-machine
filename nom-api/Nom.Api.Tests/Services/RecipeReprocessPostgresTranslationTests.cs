using System;
using System.Data.Common;
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
    /// <summary>The query every older line undo runs to find recipes rebuilt after its entries translates to Postgres SQL.</summary>
    public class RecipeReprocessPostgresTranslationTests
    {
        private readonly ITestOutputHelper _output;

        public RecipeReprocessPostgresTranslationTests(ITestOutputHelper output)
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

        [Fact]
        public async Task The_rebuilt_recipes_query_groups_in_the_database()
        {
            var capture = new CaptureAndStop();
            await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                    .UseNpgsql("Host=localhost;Database=none")
                    .AddInterceptors(capture, new NoConnection())
                    .Options,
                new HttpContextAccessor { HttpContext = new DefaultHttpContext() });

            var act = () => RecipeReprocessService.RebuiltAtAsync(db, new long[] { 1, 2, 3 });

            await act.Should().ThrowAsync<OperationCanceledException>();
            _output.WriteLine(capture.Sql);
            capture.Sql.Should().Contain("GROUP BY").And.Contain("max(");
        }
    }
}

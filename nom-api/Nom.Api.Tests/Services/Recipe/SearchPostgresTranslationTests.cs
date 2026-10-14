using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using Nom.Data;
using Nom.Orch.Interfaces;
using Nom.Orch.Services;
using Xunit;

namespace Nom.Api.Tests.Services.Recipe
{
    /// <summary>
    /// Search queries must translate to Postgres SQL with no client evaluation: the ingredient
    /// search probes each column with a chain of ILIKEs, which the trigram indexes in
    /// db/custom-objects.sql can serve. Each command is captured before it would reach a server.
    /// </summary>
    public class SearchPostgresTranslationTests
    {
        private sealed class CaptureAndStop : DbCommandInterceptor
        {
            public string? Sql { get; private set; }

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

        private static ApplicationDbContext Postgres(CaptureAndStop capture) =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>()
                    .UseNpgsql("Host=localhost;Database=none")
                    .AddInterceptors(capture, new NoConnection())
                    .Options,
                new HttpContextAccessor { HttpContext = new DefaultHttpContext() });

        [Fact]
        public async Task Search_translates_to_ilike_probes_on_each_column()
        {
            var capture = new CaptureAndStop();
            await using var db = Postgres(capture);
            var service = new RecipeOrchestrationService(db,
                new HttpContextAccessor(),
                new Mock<ICurrentUserService>().Object,
                new Mock<Nom.Orch.UtilityInterfaces.IMediaStorageService>().Object,
                new Mock<IRecipeNutritionService>().Object);

            var search = () => service.SearchIngredientsAsync("black beans");

            await search.Should().ThrowAsync<OperationCanceledException>();
            capture.Sql.Should().Contain("\"Name\" ILIKE @pattern ESCAPE '' AND i0.\"Name\" ILIKE")
                .And.Contain("\"NameNormalized\" ILIKE")
                .And.Contain("\"AliasName\" ILIKE")
                .And.Contain("UNION");
        }
    }
}

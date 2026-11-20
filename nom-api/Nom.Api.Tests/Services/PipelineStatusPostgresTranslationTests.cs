using System;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Nom.Data;
using Nom.Orch.Interfaces;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;
using Xunit;
using Xunit.Abstractions;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// The pipeline page's queries must translate to Postgres SQL with no client evaluation, and
    /// the line counts must stay one pass over the recipe lines. Each command is captured as the
    /// service would run it, before it would reach a server, and written to the test output so it
    /// can be explained against a staging copy.
    /// </summary>
    public class PipelineStatusPostgresTranslationTests
    {
        private readonly ITestOutputHelper _output;

        public PipelineStatusPostgresTranslationTests(ITestOutputHelper output)
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

        private static ApplicationDbContext Postgres(CaptureAndStop capture) =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>()
                    .UseNpgsql("Host=localhost;Database=none")
                    .AddInterceptors(capture, new NoConnection())
                    .Options,
                new HttpContextAccessor { HttpContext = new DefaultHttpContext() });

        private async Task<string> Capture(string name, Func<ApplicationDbContext, Task> run)
        {
            var capture = new CaptureAndStop();
            await using var db = Postgres(capture);
            var act = () => run(db);
            await act.Should().ThrowAsync<OperationCanceledException>();
            capture.Sql.Should().NotBeNullOrEmpty();
            _output.WriteLine($"-- {name}\n{capture.Sql};\n");
            return capture.Sql!;
        }

        [Fact]
        public async Task The_line_funnel_is_one_pass_with_filtered_counts()
        {
            var sql = await Capture("lines", db => PipelineStatusService.LineFunnelQuery(db).ToListAsync());

            sql.Should().Contain("FILTER (WHERE");
            sql.Split("FROM recipe.\"RecipeIngredient\"").Length.Should().Be(2);
            sql.Should().Contain("\"IngredientNutrient\"").And.Contain("GROUP BY");
        }

        [Fact]
        public async Task The_blocked_by_query_counts_distinct_foods_per_recipe()
        {
            var sql = await Capture("blocked", db => PipelineStatusService.BlockedByRecipeQuery(db).ToListAsync());

            sql.Should().ContainEquivalentOf("COUNT(DISTINCT");
        }

        [Fact]
        public async Task Every_query_translates()
        {
            var now = new DateTime(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);
            var matcher = new Mock<IIngredientLinkMatcher>();
            matcher.SetupGet(m => m.ModelName).Returns("qwen2.5:14b-instruct");

            await Capture("recipes", db => PipelineStatusService.RecipeQuery(db, now, now.AddDays(-30)).FirstOrDefaultAsync());
            await Capture("uncounted", db => PipelineStatusService.ApprovedWithUncountedLineQuery(db).CountAsync());
            await Capture("without-label", db => PipelineStatusService.WithoutNutritionLabelQuery(db).CountAsync());
            await Capture("auto-approvals", db => PipelineStatusService.AutoApprovalQuery(db, now).FirstOrDefaultAsync());
            await Capture("foods", db => PipelineStatusService.FoodQuery(db).FirstOrDefaultAsync());
            await Capture("curated-without-nutrition", db => PipelineStatusService.CuratedWithoutNutritionQuery(db).CountAsync());
            await Capture("unresolved-by-food", db => PipelineStatusService.UnresolvedByFoodQuery(db).ToListAsync());
            await Capture("pending-links", db => PipelineStatusService.PendingLinkFoodsQuery(db).ToListAsync());
            await Capture("rejected-links", db => PipelineStatusService.RejectedLinkFoodsQuery(db).ToListAsync());
            await Capture("item-weight-gaps", db => PipelineStatusService.ConversionGapQuery(db, RecipeNutritionService.CategoryCount).ToListAsync());
            await Capture("density-gaps", db => PipelineStatusService.ConversionGapQuery(db, RecipeNutritionService.CategoryVolume).ToListAsync());
            await Capture("proposals", db => PipelineStatusService.ProposalQuery(db, now).FirstOrDefaultAsync());
            await Capture("link-queue", db =>
            {
                var links = new IngredientLinkService(db, matcher.Object, new Mock<IIngredientLinkVerifier>().Object,
                    new MemoryCache(new MemoryCacheOptions()), NullLogger<IngredientLinkService>.Instance);
                return PipelineStatusService.LinkQueueQuery(db, links, now - IngredientLinkService.RejectionRetryAge).CountAsync();
            });
            await Capture("images", db => db.RecipeImageCandidates.GroupBy(c => c.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync());
            await Capture("journal", db => PipelineStatusService.JournalQuery(db, now).ToListAsync());
            await Capture("repair-queue", db => PipelineStatusService.RepairQueueQuery(db).CountAsync());
            await Capture("classify-queue", db => PipelineStatusService.ClassifyQueueQuery(db).CountAsync());
            await Capture("labels", db => PipelineStatusService.LastLabelQuery(db).MaxAsync());
        }
    }
}

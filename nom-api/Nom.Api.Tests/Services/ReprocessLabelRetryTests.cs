using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Nom.Data;
using Nom.Orch.Interfaces;
using Nom.Orch.Services;
using Nom.Orch.UtilityInterfaces;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// Staging checkpoint 5, Q16: a reprocess undo batch's label calculation met the API's own label writer on the same
    /// nutrition rows and fell back to one recipe at a time with a warning. A concurrency conflict is now calculated again
    /// from fresh reads; only a conflict that outlasts the retries, or another failure, falls back.
    /// </summary>
    public class ReprocessLabelRetryTests
    {
        private sealed class Levels : ILogger<RecipeReprocessService>
        {
            public List<LogLevel> Logged { get; } = new();

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Logged.Add(logLevel);
        }

        private static ApplicationDbContext NewContext() =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        private static RecipeReprocessService Service(ApplicationDbContext db, IRecipeNutritionService nutrition, Levels log) =>
            new(db, Mock.Of<ICurrentUserService>(), Mock.Of<IRecipeScraperClient>(), Mock.Of<IRecipeVettingService>(), nutrition, log);

        [Fact]
        public async Task A_conflict_with_another_label_writer_is_calculated_again_and_needs_no_fallback()
        {
            using var db = NewContext();
            var nutrition = new Mock<IRecipeNutritionService>();
            var calls = 0;
            nutrition.Setup(n => n.RecalculateManyAsync(It.IsAny<IReadOnlyCollection<long>>()))
                .Returns(() => ++calls < 3 ? Task.FromException<int>(new DbUpdateConcurrencyException("the API's label writer changed recipe.RecipeNutrition")) : Task.FromResult(2));
            var log = new Levels();

            await Service(db, nutrition.Object, log).RecalculateLabelsAsync(new long[] { 1, 2 }, null);

            nutrition.Verify(n => n.RecalculateManyAsync(It.IsAny<IReadOnlyCollection<long>>()), Times.Exactly(3));
            nutrition.Verify(n => n.RecalculateAsync(It.IsAny<long>()), Times.Never(), "the batch's labels are calculated together once the conflict is gone");
            log.Logged.Should().NotContain(LogLevel.Warning);
        }

        [Fact]
        public async Task A_conflict_that_outlasts_the_retries_falls_back_to_one_recipe_at_a_time()
        {
            using var db = NewContext();
            var nutrition = new Mock<IRecipeNutritionService>();
            nutrition.Setup(n => n.RecalculateManyAsync(It.IsAny<IReadOnlyCollection<long>>())).ThrowsAsync(new DbUpdateConcurrencyException("always"));
            nutrition.Setup(n => n.RecalculateAsync(It.IsAny<long>())).ReturnsAsync(1);
            var log = new Levels();

            await Service(db, nutrition.Object, log).RecalculateLabelsAsync(new long[] { 1, 2 }, null);

            nutrition.Verify(n => n.RecalculateManyAsync(It.IsAny<IReadOnlyCollection<long>>()), Times.Exactly(RecipeReprocessService.LabelAttempts));
            nutrition.Verify(n => n.RecalculateAsync(It.IsAny<long>()), Times.Exactly(2));
            log.Logged.Count(l => l == LogLevel.Warning).Should().Be(1);
        }
    }
}

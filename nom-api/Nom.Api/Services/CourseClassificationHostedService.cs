using Nom.Orch.Interfaces;

namespace Nom.Api.Services
{
    /// <summary>
    /// Re-derives snack / dessert / both for sweet-or-snack recipes from their added-sugar share,
    /// a few minutes after startup and then every CourseClassification:IntervalHours (default 6),
    /// so recipes pick up USDA links and recalculated nutrition as they land.
    /// CourseClassification:Enabled=false disables it.
    /// </summary>
    public class CourseClassificationHostedService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<CourseClassificationHostedService> _logger;

        public CourseClassificationHostedService(
            IServiceScopeFactory scopeFactory,
            IConfiguration configuration,
            ILogger<CourseClassificationHostedService> logger)
        {
            _scopeFactory = scopeFactory;
            _configuration = configuration;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_configuration.GetValue("CourseClassification:Enabled", true))
            {
                _logger.LogInformation("Course classification is disabled (CourseClassification:Enabled=false)");
                return;
            }

            var interval = TimeSpan.FromHours(Math.Max(1, _configuration.GetValue("CourseClassification:IntervalHours", 6)));
            var delay = TimeSpan.FromMinutes(3);
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(delay, stoppingToken);
                    using var scope = _scopeFactory.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<ICourseClassificationService>().ClassifyAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Course classification pass failed");
                }
                delay = interval;
            }
        }
    }
}

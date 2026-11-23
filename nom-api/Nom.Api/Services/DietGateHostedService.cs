using Nom.Orch.Services.Support;

namespace Nom.Api.Services
{
    /// <summary>
    /// Keeps the diet gate warm so that no household set ever probes the catalog (staging checkpoint 4, P1): at start
    /// it computes every restriction type's food probe, reads every recipe line into <see cref="DietLineIndex.Shared"/>
    /// in bounded windows and counts the retained alcohol; then, every <c>DietGate:PollSeconds</c> (default 30), it runs
    /// the warm again (the food probes of the types a restriction names, a pass, the retained counts; a type no one
    /// holds is refreshed when first read) once a food, an alias, a nutrient, a recipe line or a measurement was written
    /// (<see cref="DietGateCache.Generation"/>), a reader met a rule the index has not read (an added or edited
    /// criterion), or <see cref="DietGateCache.MaxAge"/> passed (writes from another process), never sooner than
    /// <c>DietGate:MinIntervalSeconds</c> (default 60) after the last. A pass re-reads only the recipes whose lines
    /// changed and the rules that are new. <c>DietGate:Warm=false</c> turns it off, and every household set then holds
    /// every recipe for a restriction with a pattern.
    /// </summary>
    public class DietGateHostedService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<DietGateHostedService> _logger;

        public DietGateHostedService(IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<DietGateHostedService> logger)
        {
            _scopeFactory = scopeFactory;
            _configuration = configuration;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_configuration.GetValue("DietGate:Warm", true))
            {
                _logger.LogWarning("Diet gate warm is disabled (DietGate:Warm=false); household sets hold every recipe a restriction with a pattern reads");
                return;
            }
            var poll = TimeSpan.FromSeconds(Math.Max(5, _configuration.GetValue("DietGate:PollSeconds", 30)));
            var minInterval = TimeSpan.FromSeconds(Math.Max(0, _configuration.GetValue("DietGate:MinIntervalSeconds", 60)));
            var window = Math.Max(100, _configuration.GetValue("DietGate:Window", DietLineIndex.DefaultWindow));
            var cache = DietGateCache.Shared;
            var index = DietLineIndex.Shared;
            var lastAt = DateTime.MinValue;
            var lastGeneration = -1L;
            var first = true;
            var warmedAll = false;
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<Nom.Data.ApplicationDbContext>();
                    if (!first)
                    {
                        var due = cache.Generation != lastGeneration || index.TakeRequest(db) || DateTime.UtcNow - lastAt >= cache.MaxAge;
                        if (!due || DateTime.UtcNow - lastAt < minInterval)
                        {
                            if (due) index.Request(db);
                            await Task.Delay(poll, stoppingToken);
                            continue;
                        }
                    }
                    var generation = cache.Generation;
                    var report = await index.WarmAsync(db, cache, stoppingToken, window, everyType: !warmedAll);
                    warmedAll = true;
                    lastGeneration = generation;
                    lastAt = DateTime.UtcNow;
                    _logger.LogInformation(
                        "Diet gate: {FoodProbes} food probes and {Retained} retained counts computed; line index read {Read} of {Recipes} recipes ({Lines} lines, {Rules} rules, {NewRules} new) up to recipe {ReadUpTo} in {PassSeconds:F1} s; warm {Seconds:F1} s",
                        report.FoodProbes, report.Retained, report.Pass.Read, report.Pass.Recipes, report.Pass.Lines, report.Pass.Rules, report.Pass.NewRules,
                        report.Pass.ReadUpTo, report.Pass.Took.TotalSeconds, report.Took.TotalSeconds);
                    first = false;
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Diet gate warm failed; household sets hold what is not read yet, and the warm runs again");
                    lastAt = DateTime.UtcNow;
                    first = false;
                    try
                    {
                        await Task.Delay(poll, stoppingToken);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                }
            }
        }
    }
}

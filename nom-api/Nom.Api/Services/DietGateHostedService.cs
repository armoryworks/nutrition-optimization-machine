using Microsoft.EntityFrameworkCore;
using Nom.Orch.Services.Support;
using Npgsql;

namespace Nom.Api.Services
{
    /// <summary>
    /// Runs the diet gate's build job (<see cref="DietGateStoreBuilder"/>, staging checkpoint 5, Q2): at start it brings
    /// the stored results of the current rule version up to date (the first build, or a new build beside the active one
    /// after a rule change); then, every <c>DietGate:PollSeconds</c> (default 15), it runs a cycle once a food, an alias, a
    /// nutrient, a statement, a recipe line, a recipe's servings or a measurement was written in this process
    /// (<see cref="DietGateCache.Generation"/>), and every <c>DietGate:SweepSeconds</c> (default 300) in any case for writes
    /// of another process, never sooner than <c>DietGate:MinIntervalSeconds</c> (default 20) after the last. Requests never
    /// wait on it: they read the stored rows. On Postgres one API process builds at a time (a session advisory lock); a
    /// failed cycle is logged and noted on the build, and requests keep what was stored. One log line per cycle gives the
    /// times, and one line per set built gives that set's time. <c>DietGate:Build=false</c> turns it off.
    /// </summary>
    public class DietGateHostedService : BackgroundService
    {
        private const long LockKey = 0x4e4f4d44494554;

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
            if (!_configuration.GetValue("DietGate:Build", true))
            {
                _logger.LogWarning("Diet gate build is disabled (DietGate:Build=false); requests read the stored results as they are");
                return;
            }
            var poll = TimeSpan.FromSeconds(Math.Max(5, _configuration.GetValue("DietGate:PollSeconds", 15)));
            var sweep = TimeSpan.FromSeconds(Math.Max(30, _configuration.GetValue("DietGate:SweepSeconds", 300)));
            var minInterval = TimeSpan.FromSeconds(Math.Max(0, _configuration.GetValue("DietGate:MinIntervalSeconds", 20)));
            var cache = DietGateCache.Shared;
            var builder = new DietGateStoreBuilder();
            NpgsqlConnection? held = null;
            var lastAt = DateTime.MinValue;
            var lastGeneration = -1L;
            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var db = scope.ServiceProvider.GetRequiredService<Nom.Data.ApplicationDbContext>();
                        var since = DateTime.UtcNow - lastAt;
                        var due = cache.Generation != lastGeneration || since >= sweep;
                        if (!due || since < minInterval)
                        {
                            await Task.Delay(poll, stoppingToken);
                            continue;
                        }
                        if (db.Database.IsRelational() && held == null)
                        {
                            held = await TryLockAsync(db.Database.GetConnectionString(), stoppingToken);
                            if (held == null)
                            {
                                _logger.LogInformation("Diet gate build: another process holds the build; this one only reads");
                                lastAt = DateTime.UtcNow;
                                await Task.Delay(sweep, stoppingToken);
                                continue;
                            }
                        }
                        var generation = cache.Generation;
                        var report = await builder.RunAsync(db, stoppingToken);
                        lastGeneration = generation;
                        lastAt = DateTime.UtcNow;
                        foreach (var set in report.Built)
                            _logger.LogInformation("Diet gate build {BuildId}: set {SetId} (type {TypeId}, {Reading}{Severe}) built: {Foods} foods in {FoodSeconds:F1} s, {Containing} recipes containing and {Unknown} unknown in {RecipeSeconds:F1} s",
                                report.BuildId, set.SetId, set.TypeId, set.Reading, set.Severe ? ", severe" : string.Empty, set.Foods, set.FoodSeconds, set.RecipesContaining, set.RecipesUnknown, set.RecipeSeconds);
                        _logger.LogInformation("Diet gate build {BuildId} ({Version}) {State}{Switched}: {Sets} sets; foods {FoodsDecided} of {Foods} decided in {FoodSeconds:F1} s; recipes {RecipesDecided} of {Recipes} decided in {RecipeSeconds:F1} s; cycle {Seconds:F1} s",
                            report.BuildId, report.Version, report.State, report.Switched ? " (switched)" : string.Empty, report.Sets, report.FoodsDecided, report.Foods,
                            report.FoodSeconds, report.RecipesDecided, report.Recipes, report.RecipeSeconds, report.Seconds);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Diet gate build cycle failed; requests keep reading the stored results, and the cycle runs again");
                        lastAt = DateTime.UtcNow;
                        try
                        {
                            using var scope = _scopeFactory.CreateScope();
                            await DietGateStoreBuilder.NoteFailureAsync(scope.ServiceProvider.GetRequiredService<Nom.Data.ApplicationDbContext>(), ex.GetType().Name + ": " + ex.Message, stoppingToken);
                        }
                        catch (Exception note) when (note is not OperationCanceledException)
                        {
                            _logger.LogWarning(note, "Diet gate build: the failure could not be noted on the build");
                        }
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
            finally
            {
                if (held != null) await held.DisposeAsync();
            }
        }

        private static async Task<NpgsqlConnection?> TryLockAsync(string? connectionString, CancellationToken cancellationToken)
        {
            var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand("SELECT pg_try_advisory_lock(@key)", connection);
            command.Parameters.AddWithValue("key", LockKey);
            if (await command.ExecuteScalarAsync(cancellationToken) is true) return connection;
            await connection.DisposeAsync();
            return null;
        }
    }
}

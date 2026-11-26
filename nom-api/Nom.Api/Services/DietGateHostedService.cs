using Microsoft.EntityFrameworkCore;
using Nom.Orch.Services.Support;
using Npgsql;

namespace Nom.Api.Services
{
    /// <summary>
    /// Runs the diet gate's build job (<see cref="DietGateStoreBuilder"/>, staging checkpoint 5, Q2): at start it brings
    /// the stored results of the current rule version up to date with a whole cycle (the first build, or a new build
    /// beside the active one after a rule change); then, every <c>DietGate:PollSeconds</c> (default 15), it runs a cycle
    /// over the changes once a food, an alias, a nutrient, a statement, a recipe line, a recipe's servings or a criterion
    /// was written, by this process (<see cref="DietGateCache.Generation"/>) or by any other (a committed row of the
    /// database's write log, <see cref="DietGateStoreBuilder.PendingWritesAsync"/>, written by triggers in the writer's own
    /// transaction, staging checkpoint 6, R12, review CK11-DM-1), a measurement's factor, category or container mark
    /// included (review CK11-DM-4); and a whole cycle every <c>DietGate:SweepSeconds</c> (default 900) as a backstop for a
    /// write made with the triggers disabled, never sooner than <c>DietGate:MinIntervalSeconds</c> (default 20) after the last. Requests never wait on it: they
    /// read the stored rows, and hold what a writer made undecided. On Postgres one API process builds at a time (a
    /// session advisory lock); a failed cycle is logged and noted on the build, and requests keep what was stored. One log
    /// line per cycle gives the times, and one line per set built gives that set's time. <c>DietGate:Build=false</c>
    /// turns it off; the process then only trims the write log, every <c>DietGate:SweepSeconds</c>, of rows older than
    /// <c>DietGate:WriteLogKeepSeconds</c> (default 3600), so a log no build reads does not grow without end.
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
            var poll = TimeSpan.FromSeconds(Math.Max(5, _configuration.GetValue("DietGate:PollSeconds", 15)));
            var sweep = TimeSpan.FromSeconds(Math.Max(30, _configuration.GetValue("DietGate:SweepSeconds", 900)));
            if (!_configuration.GetValue("DietGate:Build", true))
            {
                _logger.LogWarning("Diet gate build is disabled (DietGate:Build=false); requests read the stored results as they are");
                await TrimAsync(sweep, stoppingToken);
                return;
            }
            var minInterval = TimeSpan.FromSeconds(Math.Max(0, _configuration.GetValue("DietGate:MinIntervalSeconds", 20)));
            var cache = DietGateCache.Shared;
            var builder = new DietGateStoreBuilder();
            NpgsqlConnection? held = null;
            var lastAt = DateTime.MinValue;
            var lastWholeAt = DateTime.MinValue;
            var lastGeneration = -1L;
            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var db = scope.ServiceProvider.GetRequiredService<Nom.Data.ApplicationDbContext>();
                        var now = DateTime.UtcNow;
                        var whole = now - lastWholeAt >= sweep;
                        var due = whole || cache.Generation != lastGeneration || await DietGateStoreBuilder.PendingWritesAsync(db, stoppingToken);
                        if (!due || now - lastAt < minInterval)
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
                        var report = whole ? await builder.RunAsync(db, stoppingToken) : await builder.RunChangesAsync(db, stoppingToken);
                        lastGeneration = generation;
                        lastAt = DateTime.UtcNow;
                        if (whole) lastWholeAt = lastAt;
                        foreach (var set in report.Built)
                            _logger.LogInformation("Diet gate build {BuildId}: set {SetId} (type {TypeId}, {Reading}{Severe}) built: {Foods} foods in {FoodSeconds:F1} s, {Containing} recipes containing and {Unknown} unknown in {RecipeSeconds:F1} s",
                                report.BuildId, set.SetId, set.TypeId, set.Reading, set.Severe ? ", severe" : string.Empty, set.Foods, set.FoodSeconds, set.RecipesContaining, set.RecipesUnknown, set.RecipeSeconds);
                        _logger.LogInformation("Diet gate build {BuildId} ({Version}) {State}{Switched}, {Kind} cycle: {Sets} sets; foods {FoodsDecided} of {Foods} decided in {FoodSeconds:F1} s, {FoodsMoved} moved a decision; recipes {RecipesDecided} of {Recipes} decided in {RecipeSeconds:F1} s{Windowed}, {RecipesConfirmed} held by a food write released unread, {RecipesLate} written while read left for the next cycle; cycle {Seconds:F1} s",
                            report.BuildId, report.Version, report.State, report.Switched ? " (switched)" : string.Empty, whole ? "whole" : "changes", report.Sets, report.FoodsDecided, report.Foods,
                            report.FoodSeconds, report.FoodsMoved, report.RecipesDecided, report.Recipes, report.RecipeSeconds, report.Windowed ? " (read in windows)" : string.Empty, report.RecipesConfirmed, report.RecipesLate, report.Seconds);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Diet gate build cycle failed; requests keep reading the stored results, and the cycle runs again");
                        lastAt = DateTime.UtcNow;
                        lastWholeAt = DateTime.MinValue;
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

        private async Task TrimAsync(TimeSpan every, CancellationToken stoppingToken)
        {
            var age = TimeSpan.FromSeconds(Math.Max(60, _configuration.GetValue("DietGate:WriteLogKeepSeconds", 3600)));
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var removed = await DietGateStoreBuilder.TrimWritesAsync(scope.ServiceProvider.GetRequiredService<Nom.Data.ApplicationDbContext>(), age, stoppingToken);
                    if (removed > 0) _logger.LogInformation("Diet gate write log: {Removed} rows older than {Age:F0} s removed (DietGate:Build=false)", removed, age.TotalSeconds);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Diet gate write log: the trim failed and runs again");
                }
                try
                {
                    await Task.Delay(every, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
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

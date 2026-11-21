using Microsoft.Extensions.Options;
using Nom.Orch.Interfaces;
using Nom.Orch.Settings;
using Nom.Orch.UtilityInterfaces;

namespace Nom.Api.Services
{
    /// <summary>
    /// Runs the source lane (docs/architecture/recipe-sources.md): a discovery run through the
    /// scraper's budgeted job every IntervalHours, one source check per tick under the daily cap
    /// (approved sources due for their weekly re-check first), and the end of due probations.
    /// Discovered domains become candidates; only those whose check is clear are offered to an
    /// admin for approval, and nothing is ever imported from a domain until an admin approves it.
    ///
    /// Off by default: enable with SourceDiscovery:Enabled = true. Requires a configured scraper
    /// service. A failed or lost job is stored as a failed run or check, never as an empty one.
    /// </summary>
    public class SourceDiscoveryHostedService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfiguration _configuration;
        private readonly SourceAdmissionSettings _settings;
        private readonly ILogger<SourceDiscoveryHostedService> _logger;

        public SourceDiscoveryHostedService(
            IServiceScopeFactory scopeFactory,
            IConfiguration configuration,
            IOptions<SourceAdmissionSettings> settings,
            ILogger<SourceDiscoveryHostedService> logger)
        {
            _scopeFactory = scopeFactory;
            _configuration = configuration;
            _settings = settings.Value;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (_configuration.GetValue<bool?>("SourceDiscovery:AutoApprove") == true)
            {
                _logger.LogWarning("SourceDiscovery:AutoApprove is no longer read: an admin approves every source, and only a candidate whose check is clear is offered");
            }

            if (!_settings.Enabled)
            {
                _logger.LogInformation("Source discovery is disabled (SourceDiscovery:Enabled=false)");
                return;
            }

            var interval = TimeSpan.FromHours(Math.Max(1, _settings.IntervalHours));
            var tick = TimeSpan.FromMinutes(Math.Max(1, _settings.TickMinutes));
            _logger.LogInformation(
                "Source lane enabled: discovery every {Hours}h with {Seeds} seeds, at most {Checks} source checks a day, re-check every {Days} days",
                interval.TotalHours, _settings.SeedsPerRun, _settings.MaxChecksPerDay, _settings.RecheckDays);

            await SafeDelay(TimeSpan.FromMinutes(5), stoppingToken);
            var nextDiscovery = DateTime.UtcNow;
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var scraper = scope.ServiceProvider.GetRequiredService<IRecipeScraperClient>();
                    var lane = scope.ServiceProvider.GetRequiredService<ISourceAdmissionService>();
                    if (!scraper.IsConfigured)
                    {
                        _logger.LogWarning("Source lane skipped: no scraper service configured");
                    }
                    else
                    {
                        if (DateTime.UtcNow >= nextDiscovery)
                        {
                            await lane.RunDiscoveryAsync(stoppingToken);
                            nextDiscovery = DateTime.UtcNow + interval;
                        }

                        await lane.RunNextCheckAsync(stoppingToken);
                    }

                    await lane.WatchAsync(stoppingToken);
                    var rights = scope.ServiceProvider.GetRequiredService<ISourceRightsService>();
                    await rights.DeriveAsync(apply: true, staleOnly: true, cancellationToken: stoppingToken);
                    if (scope.ServiceProvider.GetRequiredService<IOptions<SourceRightsSettings>>().Value.RetentionAuto)
                    {
                        await rights.RetentionAsync(apply: true, stoppingToken);
                        await rights.PurgeRetentionAsync(apply: true, stoppingToken);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Source lane tick failed");
                }

                await SafeDelay(tick, stoppingToken);
            }
        }

        private static async Task SafeDelay(TimeSpan delay, CancellationToken token)
        {
            try
            {
                await Task.Delay(delay, token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}

using Microsoft.EntityFrameworkCore;
using Nom.Data;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;
using Nom.Orch.UtilityInterfaces;

namespace Nom.Api.Services
{
    /// <summary>
    /// Periodically asks the scraper service to discover candidate recipe sites
    /// by following outbound links from already-approved sources. Discovered
    /// domains are registered as PENDING scraping sources — which triggers the
    /// standard admin prompt (in-app + email). Nothing is ever imported from a
    /// discovered site until an admin approves it.
    ///
    /// Off by default: enable with SourceDiscovery:Enabled = true. Requires a
    /// configured scraper service and at least one approved source to seed from.
    /// </summary>
    public class SourceDiscoveryHostedService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<SourceDiscoveryHostedService> _logger;

        public SourceDiscoveryHostedService(
            IServiceScopeFactory scopeFactory,
            IConfiguration configuration,
            ILogger<SourceDiscoveryHostedService> logger)
        {
            _scopeFactory = scopeFactory;
            _configuration = configuration;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_configuration.GetValue("SourceDiscovery:Enabled", false))
            {
                _logger.LogInformation("Source discovery is disabled (SourceDiscovery:Enabled=false)");
                return;
            }

            var interval = TimeSpan.FromHours(
                Math.Max(1, _configuration.GetValue("SourceDiscovery:IntervalHours", 168)));
            var maxCandidates = Math.Clamp(
                _configuration.GetValue("SourceDiscovery:MaxCandidatesPerRun", 10), 1, 50);

            _logger.LogInformation(
                "Source discovery enabled: every {Hours}h, up to {Max} candidates per run",
                interval.TotalHours, maxCandidates);

            // First run shortly after startup, then on the interval.
            await SafeDelay(TimeSpan.FromMinutes(5), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RunDiscoveryAsync(maxCandidates, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Source discovery run failed");
                }

                await SafeDelay(interval, stoppingToken);
            }
        }

        private async Task RunDiscoveryAsync(int maxCandidates, CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var scraperClient = scope.ServiceProvider.GetRequiredService<IRecipeScraperClient>();
            var sources = scope.ServiceProvider.GetRequiredService<IScrapingSourceService>();

            if (!scraperClient.IsConfigured)
            {
                _logger.LogWarning("Source discovery skipped: no scraper service configured");
                return;
            }

            var approvedDomains = await db.ScrapingSources
                .AsNoTracking()
                .Where(s => s.Status == ScrapingSourceStatusEnum.Approved && !s.IsDeleted)
                .Select(s => s.Domain)
                .ToListAsync(cancellationToken);

            if (approvedDomains.Count == 0)
            {
                _logger.LogInformation("Source discovery skipped: no approved sources to seed from yet");
                return;
            }

            var result = await scraperClient.DiscoverAsync(approvedDomains, maxCandidates, cancellationToken);
            _logger.LogInformation(
                "Discovery returned {Count} candidates ({NoSignal} probed without signal)",
                result.Candidates.Count, result.ProbedWithoutSignal);

            foreach (var candidate in result.Candidates)
            {
                // Known domains (any status) are skipped — RequestSourceAsync is
                // idempotent per domain, so rejected sources never resurface as
                // new prompts.
                var alreadyKnown = await db.ScrapingSources
                    .AnyAsync(s => s.Domain == candidate.Domain && !s.IsDeleted, cancellationToken);
                if (alreadyKnown)
                {
                    continue;
                }

                var note =
                    $"Auto-discovered via outbound links from approved source '{candidate.DiscoveredVia}'. " +
                    $"Signal: {candidate.Signal}.";

                await sources.RequestSourceAsync(candidate.EvidenceUrl, requestedByPersonId: null, note);
                _logger.LogInformation("Proposed discovered source {Domain} (via {Via})",
                    candidate.Domain, candidate.DiscoveredVia);
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
                // shutting down
            }
        }
    }
}

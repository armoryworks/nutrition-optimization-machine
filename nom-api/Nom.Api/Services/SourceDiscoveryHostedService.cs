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
    ///
    /// With SourceDiscovery:AutoApprove = true (also off by default), candidates
    /// that pass a clean-probe gate skip the queue: the evidence URL is scraped
    /// (robots-aware, via the scraper service) and the domain is auto-whitelisted
    /// only when the probe returns a complete structured recipe with zero vetting
    /// issues over https. Anything less stays Pending for human review, and a
    /// domain an admin has rejected is never resurrected either way.
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
            var autoApprove = _configuration.GetValue("SourceDiscovery:AutoApprove", false);

            _logger.LogInformation(
                "Source discovery enabled: every {Hours}h, up to {Max} candidates per run, auto-approve {AutoApprove}",
                interval.TotalHours, maxCandidates, autoApprove ? "ON" : "off");

            // First run shortly after startup, then on the interval.
            await SafeDelay(TimeSpan.FromMinutes(5), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RunDiscoveryAsync(maxCandidates, autoApprove, stoppingToken);
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

        private async Task RunDiscoveryAsync(int maxCandidates, bool autoApprove, CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var scraperClient = scope.ServiceProvider.GetRequiredService<IRecipeScraperClient>();
            var sources = scope.ServiceProvider.GetRequiredService<IScrapingSourceService>();
            var vetting = scope.ServiceProvider.GetRequiredService<IRecipeVettingService>();

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

                if (autoApprove && await TryAutoApproveAsync(candidate, scraperClient, vetting, sources, cancellationToken))
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

        /// <summary>
        /// Probes the candidate's evidence URL through the scraper service and
        /// auto-whitelists the domain only on a clean result. Probe failures of
        /// any kind (robots disallowed, no structured data, vetting issues,
        /// scraper errors) leave the candidate on the normal Pending path.
        /// </summary>
        private async Task<bool> TryAutoApproveAsync(
            ScraperDiscoveredSource candidate,
            IRecipeScraperClient scraperClient,
            IRecipeVettingService vetting,
            IScrapingSourceService sources,
            CancellationToken cancellationToken)
        {
            try
            {
                var probe = await scraperClient.ScrapeAsync(candidate.EvidenceUrl, cancellationToken);
                var vetIssues = probe.Success && probe.Recipe != null
                    ? await vetting.VetAsync(probe.Recipe)
                    : new List<string>();

                if (!IsObviouslyFine(candidate.EvidenceUrl, probe, vetIssues))
                {
                    return false;
                }

                var reason =
                    $"AUTO-WHITELISTED by source discovery: found via outbound links from approved source " +
                    $"'{candidate.DiscoveredVia}' (signal: {candidate.Signal}); probe scrape of {candidate.EvidenceUrl} " +
                    "returned a complete structured recipe (schema.org JSON-LD) with zero vetting issues.";

                await sources.RegisterAutoApprovedSourceAsync(candidate.EvidenceUrl, reason);
                _logger.LogInformation("Auto-whitelisted discovered source {Domain} (via {Via})",
                    candidate.Domain, candidate.DiscoveredVia);
                return true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Auto-approve probe failed for {Domain}; leaving it Pending", candidate.Domain);
                return false;
            }
        }

        /// <summary>
        /// The clean-probe gate: https evidence URL, successful scrape, provenance
        /// JSON-LD present, at least one ingredient and step, and zero vetting
        /// issues. Anything less is not "obviously fine" and goes to human review.
        /// </summary>
        public static bool IsObviouslyFine(string evidenceUrl, ScraperResult probe, List<string> vetIssues)
            => Uri.TryCreate(evidenceUrl, UriKind.Absolute, out var uri)
               && uri.Scheme == Uri.UriSchemeHttps
               && probe.Success
               && probe.Recipe != null
               && !string.IsNullOrWhiteSpace(probe.Recipe.RawJsonLd)
               && probe.Recipe.Ingredients.Count > 0
               && probe.Recipe.Steps.Count > 0
               && vetIssues.Count == 0;

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

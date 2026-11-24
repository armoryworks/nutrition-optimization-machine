using Nom.Orch.Interfaces;
using Nom.Orch.Models.Curation;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;

namespace Nom.Api.Services
{
    /// <summary>
    /// Runs the clean-room prose lane (<see cref="ProseLaneService"/>) on the lane frame: it works
    /// the recipes whose prose is withheld or quarantined off by writing each method again from
    /// fact sheets, never from the source's wording, and writes a rewrite only while the frame has
    /// the lane live; in shadow or on probation it decides and records without writing. Each sweep
    /// is followed by the planted-fault canaries when due and by the lane's measurement (audit
    /// sample, evaluation), which qualifies, pauses and undoes it.
    ///
    /// Off unless Ai:BatchOllamaUrl is configured. The writer is Ai:RewriteModel or the roster's
    /// reader A, the judge reader B and the auditor the roster's auditor; the lane refuses to run
    /// when two of them share a family. ProseRewrite:IntervalMinutes (default 10) and
    /// ProseRewrite:BatchSize (default 25) set the pace; while a sweep comes back full the next
    /// starts after ProseRewrite:PauseSeconds (default 5) instead (ProseRewrite:DrainWhileFull,
    /// default on only when Ai:Lanes:prose-rewrite:MaxItemsPerDay is above 0). A model host outage
    /// charges the recipe nothing, ends the sweep and is reported to the <see cref="AiLaneGate"/>
    /// (lane "prose-rewrite"), which every sweep passes.
    /// </summary>
    public class ProseRewriteHostedService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfiguration _configuration;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<ProseRewriteHostedService> _logger;
        private readonly AiLaneGate _gate;

        public ProseRewriteHostedService(
            IServiceScopeFactory scopeFactory,
            IConfiguration configuration,
            IHttpClientFactory httpClientFactory,
            ILogger<ProseRewriteHostedService> logger,
            AiLaneGate gate)
        {
            _gate = gate;
            _scopeFactory = scopeFactory;
            _configuration = configuration;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var baseUrl = (_configuration["Ai:BatchOllamaUrl"] ?? string.Empty).TrimEnd('/');
            if (string.IsNullOrEmpty(baseUrl))
            {
                _logger.LogInformation("Prose rewrite is disabled (Ai:BatchOllamaUrl not configured)");
                _gate.Disabled(AiLanes.ProseRewrite, "Ai:BatchOllamaUrl not configured");
                return;
            }

            var models = ProseLaneModels.From(_configuration);
            if (models.Problem is { } problem)
            {
                _logger.LogError("Prose rewrite is disabled: {Problem}", problem);
                _gate.Disabled(AiLanes.ProseRewrite, problem);
                return;
            }

            var interval = TimeSpan.FromMinutes(Math.Max(1, _configuration.GetValue("ProseRewrite:IntervalMinutes", 10)));
            var batchSize = Math.Clamp(_configuration.GetValue("ProseRewrite:BatchSize", 25), 1, 100);
            var drain = AiLanes.ProseDrainWhileFull(_configuration);
            var drainPause = TimeSpan.FromSeconds(Math.Max(0, _configuration.GetValue("ProseRewrite:PauseSeconds", 5)));

            _logger.LogInformation(
                "Prose rewrite (clean-room lane {Version}) enabled: {Url}, writer {Writer}, judge {Judge}, auditor {Auditor}, every {Minutes}m, up to {Batch} recipes per sweep",
                models.Version, baseUrl, models.Writer, models.Judge, models.Auditor, interval.TotalMinutes, batchSize);

            await SafeDelay(TimeSpan.FromMinutes(3), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                if (!await _gate.WaitAsync(AiLanes.ProseRewrite, stoppingToken)) return;
                var delay = interval;
                try
                {
                    var result = await RunOnceAsync(baseUrl, models, batchSize, stoppingToken);
                    if (result.Outage != null)
                    {
                        _gate.Failed(AiLanes.ProseRewrite, result.Outage);
                    }
                    else
                    {
                        _gate.Passed(AiLanes.ProseRewrite, result.Seen, result.Seen == 0);
                        if (drain && result.Seen >= batchSize) delay = drainPause;
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Prose rewrite sweep failed");
                    _gate.Failed(AiLanes.ProseRewrite, ex);
                }

                await SafeDelay(delay, stoppingToken);
            }
        }

        /// <summary>One sweep in the mode the frame gives the lane, then the canaries when due and the lane's measurement; a model host outage ends it early.</summary>
        internal async Task<ProseSweepResult> RunOnceAsync(string baseUrl, ProseLaneModels models, int batchSize, CancellationToken cancellationToken)
        {
            var http = _httpClientFactory.CreateClient(nameof(ProseRewriteHostedService));
            http.Timeout = TimeSpan.FromSeconds(600);
            var writer = new OllamaProseWriter(http, baseUrl, models.Writer);
            var judge = new OllamaProseStepJudge(http, baseUrl, models.Judge);
            var auditor = new OllamaProseJudge(http, baseUrl, models.Auditor);

            var mode = await _gate.WriteModeAsync(AiLanes.ProseRewrite, ProseLaneService.Tier, cancellationToken);
            using var scope = _scopeFactory.CreateScope();
            var lane = scope.ServiceProvider.GetRequiredService<ProseLaneService>();
            var result = await lane.SweepAsync(mode, models, writer, judge, batchSize, cancellationToken);
            if (result.Outage != null) return result;

            try
            {
                if (await lane.CanariesDueAsync(models.Version, cancellationToken))
                    await lane.RunCanariesAsync(models, judge, cancellationToken);
                var verdict = await lane.MeasureAsync(mode, models, auditor, cancellationToken: cancellationToken);
                if (verdict.Journal != null)
                    _logger.LogWarning("Prose rewrite lane: {Journal} ({Mode}): {Reason}", verdict.Journal, verdict.Mode, verdict.Reason);
            }
            catch (HttpRequestException ex) when (AiLanes.IsOutage(ex))
            {
                result.Outage = ex;
            }

            return result;
        }

        private static async Task SafeDelay(TimeSpan delay, CancellationToken token)
        {
            try
            {
                await Task.Delay(delay, token);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }
}

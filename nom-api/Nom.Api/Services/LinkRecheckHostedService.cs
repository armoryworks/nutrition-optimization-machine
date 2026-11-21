using Microsoft.Extensions.Options;
using Nom.Data.Curation;
using Nom.Orch.Interfaces;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;
using Nom.Orch.Settings;

namespace Nom.Api.Services
{
    /// <summary>
    /// The link re-check lane (plan C3): after each start, and then every LinkRecheck:IntervalHours
    /// (default 24), it runs each rule set of <see cref="LinkRecheckRules.Sets"/> in order as a tier of
    /// lane <c>link-recheck</c> on the lane frame, over the wordings it has not decided under the set's
    /// signature in the last 30 days, at most LinkRecheck:MaxPerDay (default 2000) a set in 24 hours
    /// and LinkRecheck:BatchSize (default 2000, so one batch a set a day) a batch. A set writes only
    /// while the frame holds it live; in shadow, and while paused, it decides and records. After each batch the roster's
    /// auditor reads a sample of the set's moves, reader B confirms its flags, and the frame
    /// evaluates the tier, so a set pauses and undoes its own runs by itself. Every batch passes the
    /// <see cref="AiLaneGate"/> (lane "link-recheck", switch ai-pause-link-recheck).
    ///
    /// Off unless LinkRecheck:Enabled=true. The withdrawal of waiting picks and the operator's
    /// applies are admin calls (api/FoodCatalog/link-recheck), not this lane.
    /// </summary>
    public class LinkRecheckHostedService : BackgroundService
    {
        public const string HttpClientName = nameof(LinkRecheckHostedService);

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<LinkRecheckHostedService> _logger;
        private readonly AiLaneGate _gate;
        private readonly IHttpClientFactory _http;
        private readonly LaneModelRoster _roster;

        public LinkRecheckHostedService(IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<LinkRecheckHostedService> logger, AiLaneGate gate,
            IHttpClientFactory http, IOptions<LaneModelRoster> roster)
        {
            _scopeFactory = scopeFactory;
            _configuration = configuration;
            _logger = logger;
            _gate = gate;
            _http = http;
            _roster = roster.Value;
        }

        public static bool Enabled(IConfiguration configuration) => configuration.GetValue("LinkRecheck:Enabled", false);

        /// <summary>The roster's auditor and reader B as the link re-check's auditor and confirming check, on the batch host; null with the reason when they cannot audit.</summary>
        public static (LinkRecheckAudit? Auditor, LinkRecheckAudit? Check, string? Problem) Auditors(IConfiguration configuration, LaneModelRoster roster, HttpClient http)
        {
            var host = configuration["Ai:BatchOllamaUrl"] is { Length: > 0 } batch ? batch : configuration["Ai:OllamaUrl"] ?? string.Empty;
            var strict = configuration.GetValue("Ai:StrictSchema", true);
            var auditor = new OllamaIngredientLinkVerifier(http, host, roster.Auditor, strict);
            var confirmer = new OllamaIngredientLinkVerifier(http, host, roster.ReaderB, strict);
            return LinkRecheckAudit.Problem(auditor, confirmer) is { } problem
                ? (null, null, problem)
                : (new LinkRecheckAudit(auditor), new LinkRecheckAudit(confirmer), null);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!Enabled(_configuration))
            {
                _logger.LogInformation("Link re-check lane is off (LinkRecheck:Enabled is not true)");
                _gate.Disabled(AiLanes.LinkRecheck, "LinkRecheck:Enabled is not true");
                return;
            }

            var batchSize = Math.Clamp(_configuration.GetValue("LinkRecheck:BatchSize", 2000), 1, 50000);
            var cap = Math.Max(0, _configuration.GetValue("LinkRecheck:MaxPerDay", 2000));
            var interval = TimeSpan.FromHours(Math.Max(1, _configuration.GetValue("LinkRecheck:IntervalHours", 24)));
            var pause = TimeSpan.FromSeconds(Math.Max(0, _configuration.GetValue("LinkRecheck:PauseSeconds", 5)));
            var backoff = TimeSpan.FromMinutes(5);
            _logger.LogInformation("Link re-check lane on: {Batch} wordings per batch, at most {Cap} a set in 24 h, a round every {Hours} h",
                batchSize, cap, interval.TotalHours);

            if (!await Delay(TimeSpan.FromMinutes(5), stoppingToken)) return;

            while (!stoppingToken.IsCancellationRequested)
            {
                if (!await _gate.WaitAsync(AiLanes.LinkRecheck, stoppingToken)) return;
                var delay = interval;
                try
                {
                    var taken = 0;
                    var more = false;
                    foreach (var set in LinkRecheckRules.Sets)
                    {
                        if (await _gate.PausedAsync(AiLanes.LinkRecheck)) break;
                        using var scope = _scopeFactory.CreateScope();
                        var lane = scope.ServiceProvider.GetRequiredService<ILinkRecheckService>();
                        var used = await lane.DecidedTodayAsync(set, stoppingToken);
                        if (used >= cap) continue;
                        var mode = await _gate.WriteModeAsync(AiLanes.LinkRecheck, set, stoppingToken);
                        var batch = await lane.RunLaneBatchAsync(set, mode, Math.Min(batchSize, cap - used), stoppingToken);
                        if (batch.Groups == 0) continue;
                        taken += batch.Groups;
                        more |= batch.Remaining > 0;

                        var faults = new List<string>();
                        var attempted = batch.LinesMoved + batch.LinesUnlinked + batch.LinesSkipped;
                        if (batch.Applied && attempted >= LinkRecheckService.MinimumLinesForShare && (double)batch.LinesSkipped / attempted > LinkRecheckService.SkippedShareToPause)
                            faults.Add($"{batch.LinesSkipped} of {attempted} lines were no longer where the batch found them");
                        var (auditor, check, problem) = Auditors(_configuration, _roster, _http.CreateClient(HttpClientName));
                        if (problem != null) _logger.LogInformation("Link re-check {Set}: no audit ({Problem})", set, problem);
                        var verdict = await lane.AuditAndEvaluateAsync(set, faults, auditor, check, stoppingToken);
                        _logger.LogInformation("Link re-check lane {Set}: {Mode} ({Reason})", set, verdict.Mode, verdict.Reason);
                        if (!await Delay(pause, stoppingToken)) return;
                    }
                    _gate.Passed(AiLanes.LinkRecheck, taken, idle: taken == 0);
                    if (more) delay = pause;
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Link re-check lane round failed");
                    _gate.Failed(AiLanes.LinkRecheck, ex);
                    delay = backoff;
                }

                if (!await Delay(delay, stoppingToken)) return;
            }
        }

        private static async Task<bool> Delay(TimeSpan delay, CancellationToken token)
        {
            try
            {
                await Task.Delay(delay, token);
                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }
    }
}

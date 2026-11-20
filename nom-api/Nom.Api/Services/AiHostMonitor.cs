using System.Collections.Concurrent;
using System.Text.Json;
using Nom.Orch.Interfaces;

namespace Nom.Api.Services
{
    /// <summary>
    /// The shared breaker for the model hosts the API's lanes call, fed by <see cref="AiLaneHttpHandler"/>.
    /// A host is marked down after Ai:Breaker:FailuresToOpen (default 3) consecutive failed calls
    /// (no connection, a timeout or a 5xx) and is marked up again only when GET /api/tags answers and
    /// lists every model the lanes have had an answer from on it. A 404 for a model the host does not
    /// have fails only the lane that asked for it: it is listed as missing and never holds the host
    /// or the other lanes. While a host
    /// is down its calls fail at once without leaving the API, and it is probed at most every
    /// Ai:Breaker:ProbeMinutes (default 5). Also keeps per-lane call counts and latencies since start.
    /// </summary>
    public sealed class AiHostMonitor : IAiHostHealth
    {
        private const int LatencySamples = 500;

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<AiHostMonitor> _logger;
        private readonly int _failuresToOpen;
        private readonly TimeSpan _probeEvery;
        private readonly ConcurrentDictionary<string, HostState> _hosts = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, LaneCalls> _lanes = new(StringComparer.OrdinalIgnoreCase);

        public AiHostMonitor(IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<AiHostMonitor> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _failuresToOpen = Math.Max(1, configuration.GetValue("Ai:Breaker:FailuresToOpen", 3));
            _probeEvery = TimeSpan.FromMinutes(Math.Max(1, configuration.GetValue("Ai:Breaker:ProbeMinutes", 5)));
        }

        public const string ProbeClientName = "ai-host-probe";

        public DateTime CountingSince { get; } = DateTime.UtcNow;

        public bool IsDown(string host) => _hosts.TryGetValue(host, out var state) && state.DownSince != null;

        /// <summary>The hosts a lane has called since start.</summary>
        public IReadOnlyList<string> HostsOf(string lane) =>
            _lanes.TryGetValue(lane, out var calls) ? calls.Hosts.Keys.ToList() : Array.Empty<string>();

        public void Record(string lane, string host, string? model, bool healthy, string? error, TimeSpan elapsed, bool modelMissing = false)
        {
            var calls = _lanes.GetOrAdd(lane, _ => new LaneCalls());
            var state = _hosts.GetOrAdd(host, _ => new HostState());
            var now = DateTime.UtcNow;
            lock (calls)
            {
                calls.Hosts[host] = true;
                if (model != null) calls.Model = model;
                calls.Calls++;
                calls.LastCall = now;
                if (healthy)
                {
                    calls.LastSuccess = now;
                    calls.Latencies.Enqueue(elapsed.TotalMilliseconds);
                    while (calls.Latencies.Count > LatencySamples) calls.Latencies.Dequeue();
                }
                else
                {
                    calls.Errors++;
                    calls.LastError = error;
                    calls.LastErrorAt = now;
                }
            }

            lock (state)
            {
                if (model != null) state.Models.Add(model);
                if (modelMissing)
                {
                    if (model != null)
                    {
                        state.Answered.Remove(model);
                        state.NotFound.Add(model);
                    }
                    return;
                }
                if (model != null && healthy)
                {
                    state.Answered.Add(model);
                    state.NotFound.Remove(model);
                }
                if (healthy)
                {
                    state.ConsecutiveFailures = 0;
                    return;
                }
                state.LastError = error;
                if (++state.ConsecutiveFailures < _failuresToOpen || state.DownSince != null) return;
                state.DownSince = now;
                state.LastProbe = now;
            }
            _logger.LogWarning("AI host {Host} marked down after {Count} failed calls in a row (last: {Error}); lanes wait and it is probed every {Minutes} min",
                host, _failuresToOpen, error, _probeEvery.TotalMinutes);
        }

        /// <summary>
        /// Probes every down host the lane uses whose last probe is older than the probe interval.
        /// True when none of the lane's hosts is down afterwards.
        /// </summary>
        public async Task<bool> RecoverAsync(string lane, CancellationToken cancellationToken)
        {
            var allUp = true;
            foreach (var host in HostsOf(lane))
            {
                if (!_hosts.TryGetValue(host, out var state)) continue;
                bool due;
                lock (state)
                {
                    if (state.DownSince == null) continue;
                    due = DateTime.UtcNow - (state.LastProbe ?? DateTime.MinValue) >= _probeEvery;
                }
                if (!due || !await ProbeAsync(host, state, cancellationToken)) allUp = false;
            }
            return allUp;
        }

        public async Task<bool> AnswersAsync(string lane, CancellationToken cancellationToken = default)
        {
            var hosts = HostsOf(lane);
            foreach (var host in hosts)
            {
                if (!await ProbeAsync(host, _hosts.GetOrAdd(host, _ => new HostState()), cancellationToken)) return false;
            }
            return hosts.Count > 0;
        }

        public IReadOnlyList<AiHostStatusModel> Hosts()
        {
            return _hosts.Select(h =>
            {
                lock (h.Value)
                {
                    return new AiHostStatusModel
                    {
                        Host = h.Key,
                        Up = h.Value.DownSince == null,
                        DownSince = h.Value.DownSince,
                        ConsecutiveFailures = h.Value.ConsecutiveFailures,
                        LastError = h.Value.LastError,
                        LastProbe = h.Value.LastProbe,
                        ModelsAsked = h.Value.Models.OrderBy(m => m).ToList(),
                        ModelsInstalled = h.Value.Installed,
                        ModelsMissing = h.Value.Missing.Union(h.Value.NotFound, StringComparer.OrdinalIgnoreCase).OrderBy(m => m).ToList(),
                    };
                }
            }).OrderBy(h => h.Host).ToList();
        }

        internal LaneCallsSnapshot Calls(string lane)
        {
            if (!_lanes.TryGetValue(lane, out var calls)) return new LaneCallsSnapshot(null, 0, 0, null, null, null, null, null, null);
            lock (calls)
            {
                var sorted = calls.Latencies.OrderBy(l => l).ToList();
                return new LaneCallsSnapshot(calls.Model, calls.Calls, calls.Errors, calls.LastCall, calls.LastSuccess, calls.LastError, calls.LastErrorAt,
                    Percentile(sorted, 0.5), Percentile(sorted, 0.95));
            }
        }

        private async Task<bool> ProbeAsync(string host, HostState state, CancellationToken cancellationToken)
        {
            List<string> asked;
            lock (state)
            {
                state.LastProbe = DateTime.UtcNow;
                asked = state.Answered.ToList();
            }

            string? failure;
            List<string> installed = new();
            try
            {
                var http = _httpClientFactory.CreateClient(ProbeClientName);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                using var response = await http.GetAsync($"{host}/api/tags", timeout.Token);
                if (!response.IsSuccessStatusCode)
                {
                    failure = $"probe HTTP {(int)response.StatusCode}";
                }
                else
                {
                    using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
                    if (body.RootElement.TryGetProperty("models", out var models) && models.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var m in models.EnumerateArray())
                        {
                            if (m.TryGetProperty("name", out var name) && name.GetString() is { } tag) installed.Add(tag);
                        }
                    }
                    var missing = asked.Where(a => !installed.Any(i => SameTag(a, i))).ToList();
                    lock (state)
                    {
                        state.Installed = installed;
                        state.Missing = missing;
                    }
                    failure = missing.Count > 0 ? "model not installed: " + string.Join(", ", missing) : null;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                failure = "probe " + ex.GetType().Name + ": " + ex.Message;
            }

            bool recovered;
            lock (state)
            {
                if (failure != null)
                {
                    state.LastError = failure;
                    return false;
                }
                recovered = state.DownSince != null;
                state.DownSince = null;
                state.ConsecutiveFailures = 0;
            }
            if (recovered) _logger.LogInformation("AI host {Host} answers again with every model the lanes use; lanes resume", host);
            return true;
        }

        private static bool SameTag(string asked, string installed) =>
            string.Equals(asked, installed, StringComparison.OrdinalIgnoreCase)
            || (!asked.Contains(':') && string.Equals(asked + ":latest", installed, StringComparison.OrdinalIgnoreCase));

        private static double? Percentile(IReadOnlyList<double> sorted, double p) =>
            sorted.Count == 0 ? null : sorted[Math.Min(sorted.Count - 1, (int)Math.Ceiling(p * sorted.Count) - 1)];

        private sealed class HostState
        {
            public int ConsecutiveFailures;
            public DateTime? DownSince;
            public DateTime? LastProbe;
            public string? LastError;
            public HashSet<string> Models { get; } = new(StringComparer.OrdinalIgnoreCase);
            public HashSet<string> NotFound { get; } = new(StringComparer.OrdinalIgnoreCase);
            public HashSet<string> Answered { get; } = new(StringComparer.OrdinalIgnoreCase);
            public IReadOnlyList<string> Installed = Array.Empty<string>();
            public IReadOnlyList<string> Missing = Array.Empty<string>();
        }

        private sealed class LaneCalls
        {
            public ConcurrentDictionary<string, bool> Hosts { get; } = new(StringComparer.OrdinalIgnoreCase);
            public string? Model;
            public long Calls;
            public long Errors;
            public DateTime? LastCall;
            public DateTime? LastSuccess;
            public string? LastError;
            public DateTime? LastErrorAt;
            public Queue<double> Latencies { get; } = new();
        }

        internal sealed record LaneCallsSnapshot(string? Model, long Calls, long Errors, DateTime? LastCall, DateTime? LastSuccess,
            string? LastError, DateTime? LastErrorAt, double? LatencyMedianMs, double? LatencyP95Ms);
    }
}

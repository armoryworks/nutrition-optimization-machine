using System.Collections.Concurrent;
using Nom.Data;
using Nom.Orch.Interfaces;
using Nom.Orch.Services.Support;

namespace Nom.Api.Services
{
    /// <summary>
    /// The one gate every model lane in the API passes before each batch. In order it checks:
    /// the pause switches (platform features ai-pause-all and ai-pause-{lane}, toggled on Admin,
    /// Platform features; no row means running), the quiet hours (<see cref="AiQuietHours"/>), the
    /// shared host breaker (<see cref="AiHostMonitor"/>), and the lane's daily cap
    /// Ai:Lanes:{lane}:MaxItemsPerDay (default 0, no cap; counted over the last 24 hours from the
    /// lane's own rows, see <see cref="AiLaneLedger"/>). A blocked lane waits and re-checks every
    /// Ai:Gate:PollSeconds (default 30), so a switch takes effect within that time. The gate only
    /// ever stops work. Each lane logs a state line when its state changes and at least hourly.
    /// </summary>
    public sealed class AiLaneGate : IAiLaneStatus
    {
        private static readonly TimeSpan StateLogEvery = TimeSpan.FromHours(1);
        private static readonly TimeSpan LedgerRefresh = TimeSpan.FromMinutes(1);
        private static readonly TimeSpan Day = TimeSpan.FromHours(24);

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfiguration _configuration;
        private readonly AiHostMonitor _monitor;
        private readonly ILogger<AiLaneGate> _logger;
        private readonly TimeSpan _poll;
        private readonly ConcurrentDictionary<string, LaneState> _lanes = new(StringComparer.OrdinalIgnoreCase);

        public AiLaneGate(IServiceScopeFactory scopeFactory, IConfiguration configuration, AiHostMonitor monitor, ILogger<AiLaneGate> logger)
        {
            _scopeFactory = scopeFactory;
            _configuration = configuration;
            _monitor = monitor;
            _logger = logger;
            _poll = TimeSpan.FromSeconds(Math.Max(1, configuration.GetValue("Ai:Gate:PollSeconds", 30)));
        }

        /// <summary>Waits until the lane may run its next batch; false when cancelled.</summary>
        public async Task<bool> WaitAsync(string lane, CancellationToken cancellationToken)
        {
            var state = Lane(lane);
            while (!cancellationToken.IsCancellationRequested)
            {
                var (next, reason, wait) = await EvaluateAsync(lane, state, cancellationToken);
                Enter(lane, state, next, reason);
                if (next == AiLaneState.Running) return true;
                try
                {
                    await Task.Delay(wait, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return false;
                }
            }
            return false;
        }

        /// <summary>
        /// True when a pause switch stops the lane now, without waiting; a lane working through a
        /// backlog in batches checks it between batches so a pause stops it at the next one.
        /// </summary>
        public async Task<bool> PausedAsync(string lane)
        {
            if (await PausedByAsync(lane) is not { } key) return false;
            Enter(lane, Lane(lane), AiLaneState.Paused, $"paused by the platform feature {key}");
            return true;
        }

        /// <summary>Records a finished batch: how many items it handled and whether the lane found nothing to do.</summary>
        public void Passed(string lane, int items, bool idle)
        {
            var state = Lane(lane);
            var now = DateTime.UtcNow;
            lock (state)
            {
                state.LastPass = now;
                state.LastPassItems = items;
                if (items > 0)
                {
                    state.Items.Enqueue((now, items));
                    state.ItemsSinceLedger += items;
                }
                Trim(state, now);
                SetState(state, idle ? AiLaneState.Idle : AiLaneState.Running, null, now);
            }
        }

        public void Failed(string lane, Exception exception)
        {
            var state = Lane(lane);
            var now = DateTime.UtcNow;
            lock (state)
            {
                state.LastError = exception.GetType().Name + ": " + exception.Message;
                state.LastErrorAt = now;
                SetState(state, AiLaneState.Failing, state.LastError, now);
            }
        }

        public void Disabled(string lane, string reason)
        {
            var state = Lane(lane);
            lock (state) SetState(state, AiLaneState.Disabled, reason, DateTime.UtcNow);
        }

        public IReadOnlyList<AiLaneStatusModel> GetLanes()
        {
            var now = DateTime.UtcNow;
            return AiLanes.All.Select(l =>
            {
                var state = Lane(l.Lane);
                var calls = _monitor.Calls(l.Lane);
                var cap = Cap(l.Lane);
                lock (state)
                {
                    Trim(state, now);
                    var fromLedger = cap > 0 && state.LedgerCount != null;
                    return new AiLaneStatusModel
                    {
                        Lane = l.Lane,
                        State = state.State,
                        StateSince = state.StateSince,
                        StateReason = state.Reason,
                        Model = calls.Model,
                        LastPass = state.LastPass,
                        LastPassItems = state.LastPassItems,
                        LastError = state.LastError ?? calls.LastError,
                        LastErrorAt = state.LastErrorAt ?? calls.LastErrorAt,
                        ItemsToday = fromLedger ? state.LedgerCount!.Value + state.ItemsSinceLedger : state.Items.Sum(i => i.Count),
                        ItemsTodayFromLedger = fromLedger,
                        DailyCap = cap > 0 ? cap : null,
                        ModelCalls = calls.Calls,
                        ModelCallErrors = calls.Errors,
                        LastCall = calls.LastCall,
                        LastSuccess = calls.LastSuccess,
                        LatencyMedianMs = calls.LatencyMedianMs,
                        LatencyP95Ms = calls.LatencyP95Ms,
                        CountingSince = _monitor.CountingSince,
                    };
                }
            }).ToList();
        }

        public IReadOnlyList<AiHostStatusModel> GetHosts() => _monitor.Hosts();

        private async Task<(string State, string? Reason, TimeSpan Wait)> EvaluateAsync(string lane, LaneState state, CancellationToken cancellationToken)
        {
            if (await PausedByAsync(lane) is { } key)
            {
                return (AiLaneState.Paused, $"paused by the platform feature {key}", _poll);
            }

            if (AiQuietHours.Remaining(_configuration, DateTimeOffset.UtcNow) is { } left)
            {
                return (AiLaneState.QuietHours, $"quiet hours, {left.TotalMinutes:0} min left to leave the GPU to batch jobs", left < _poll ? left : _poll);
            }

            var down = _monitor.HostsOf(lane).Where(_monitor.IsDown).ToList();
            if (down.Count > 0 && !await _monitor.RecoverAsync(lane, cancellationToken))
            {
                return (AiLaneState.HostDown, "model host down: " + string.Join(", ", _monitor.HostsOf(lane).Where(_monitor.IsDown)), _poll);
            }

            var cap = Cap(lane);
            if (cap > 0)
            {
                var used = await ItemsTodayAsync(lane, state, cancellationToken);
                if (used >= cap)
                {
                    return (AiLaneState.AtCap, $"daily cap reached: {used} items in 24 h against Ai:Lanes:{lane}:MaxItemsPerDay={cap}", _poll * 10);
                }
            }

            return (AiLaneState.Running, null, TimeSpan.Zero);
        }

        private async Task<string?> PausedByAsync(string lane)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var features = scope.ServiceProvider.GetRequiredService<IPlatformFeatureService>();
                if (await features.IsEnabledAsync(AiLanes.PauseAllKey)) return AiLanes.PauseAllKey;
                var key = AiLanes.PauseKey(lane);
                return await features.IsEnabledAsync(key) ? key : null;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "AI lane {Lane}: could not read its pause switches; treating it as not paused", lane);
                return null;
            }
        }

        private async Task<int> ItemsTodayAsync(string lane, LaneState state, CancellationToken cancellationToken)
        {
            var now = DateTime.UtcNow;
            lock (state)
            {
                if (state.LedgerAt is { } at && now - at < LedgerRefresh) return state.LedgerCount!.Value + state.ItemsSinceLedger;
                if (state.NoLedger)
                {
                    Trim(state, now);
                    return state.Items.Sum(i => i.Count);
                }
            }

            int? counted;
            try
            {
                using var scope = _scopeFactory.CreateScope();
                counted = await AiLaneLedger.CountSinceAsync(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(), lane, now - Day, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "AI lane {Lane}: could not count its items in the last 24 hours; using the last count and what it has done since", lane);
                lock (state)
                {
                    Trim(state, now);
                    var inMemory = state.Items.Sum(i => i.Count);
                    return state.LedgerCount is { } last ? Math.Max(last + state.ItemsSinceLedger, inMemory) : inMemory;
                }
            }

            lock (state)
            {
                if (counted == null)
                {
                    state.NoLedger = true;
                    Trim(state, now);
                    return state.Items.Sum(i => i.Count);
                }
                state.LedgerCount = counted;
                state.LedgerAt = now;
                state.ItemsSinceLedger = 0;
                return counted.Value;
            }
        }

        private int Cap(string lane) => Math.Max(0, _configuration.GetValue($"Ai:Lanes:{lane}:MaxItemsPerDay", 0));

        private void Enter(string lane, LaneState state, string next, string? reason)
        {
            var now = DateTime.UtcNow;
            bool changed, logState;
            lock (state)
            {
                var wasBlocked = IsBlocked(state.State);
                changed = next == AiLaneState.Running ? wasBlocked : next != state.State;
                if (changed || next != AiLaneState.Running) SetState(state, next, reason, now);
                logState = changed || now - state.LastStateLog >= StateLogEvery;
                if (logState) state.LastStateLog = now;
            }
            if (!logState) return;

            var lanes = GetLanes().First(l => l.Lane == lane);
            _logger.LogInformation(
                "AI lane {Lane}: {State}{Reason}; last pass {LastPass} with {Items} items; {ItemsToday} items in 24 h{Cap}; {Calls} model calls, {Errors} failed since {Since:u}",
                lane, lanes.State, reason == null ? string.Empty : " (" + reason + ")",
                lanes.LastPass?.ToString("u") ?? "never", lanes.LastPassItems, lanes.ItemsToday,
                lanes.DailyCap is { } cap ? $" (cap {cap})" : string.Empty, lanes.ModelCalls, lanes.ModelCallErrors, lanes.CountingSince);
        }

        private static bool IsBlocked(string state) =>
            state is AiLaneState.Paused or AiLaneState.QuietHours or AiLaneState.HostDown or AiLaneState.AtCap or AiLaneState.NotStarted;

        private static void SetState(LaneState state, string next, string? reason, DateTime now)
        {
            if (state.State != next) state.StateSince = now;
            state.State = next;
            state.Reason = reason;
        }

        private static void Trim(LaneState state, DateTime now)
        {
            while (state.Items.Count > 0 && now - state.Items.Peek().At >= Day) state.Items.Dequeue();
        }

        private LaneState Lane(string lane) => _lanes.GetOrAdd(lane, _ => new LaneState());

        private sealed class LaneState
        {
            public string State = AiLaneState.NotStarted;
            public DateTime? StateSince;
            public string? Reason;
            public DateTime? LastPass;
            public int LastPassItems;
            public string? LastError;
            public DateTime? LastErrorAt;
            public DateTime LastStateLog = DateTime.MinValue;
            public Queue<(DateTime At, int Count)> Items { get; } = new();
            public int? LedgerCount;
            public DateTime? LedgerAt;
            public int ItemsSinceLedger;
            public bool NoLedger;
        }
    }
}

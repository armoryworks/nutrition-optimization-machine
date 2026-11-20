namespace Nom.Api.Services
{
    /// <summary>
    /// Read-only view of the model lanes for a status page, served from memory by <see cref="AiLaneGate"/>.
    /// Counts reset when the API restarts; <see cref="AiLaneStatusModel.CountingSince"/> says since when.
    /// </summary>
    public interface IAiLaneStatus
    {
        /// <summary>One entry per lane in <see cref="Nom.Orch.Services.Support.AiLanes.All"/> order.</summary>
        IReadOnlyList<AiLaneStatusModel> GetLanes();

        /// <summary>Every model host a lane has called since start, with its breaker state.</summary>
        IReadOnlyList<AiHostStatusModel> GetHosts();
    }

    /// <summary>What a lane is doing now and how far it has got.</summary>
    public sealed class AiLaneStatusModel
    {
        public string Lane { get; init; } = string.Empty;

        /// <summary>One of <see cref="AiLaneState"/>.</summary>
        public string State { get; init; } = AiLaneState.NotStarted;

        public DateTime? StateSince { get; init; }

        /// <summary>Why the lane is in its state, in words: the switch, the host, the cap.</summary>
        public string? StateReason { get; init; }

        /// <summary>The model the lane last asked for.</summary>
        public string? Model { get; init; }

        public DateTime? LastPass { get; init; }
        public int LastPassItems { get; init; }
        public string? LastError { get; init; }
        public DateTime? LastErrorAt { get; init; }

        /// <summary>
        /// Items in the last 24 hours. From the lane's own ledger rows when a daily cap is set
        /// (so a restart does not reset it), otherwise counted in memory since start.
        /// </summary>
        public int ItemsToday { get; init; }

        public bool ItemsTodayFromLedger { get; init; }

        /// <summary>Ai:Lanes:{lane}:MaxItemsPerDay; null when the lane has no cap.</summary>
        public int? DailyCap { get; init; }

        public long ModelCalls { get; init; }
        public long ModelCallErrors { get; init; }
        public DateTime? LastCall { get; init; }
        public DateTime? LastSuccess { get; init; }
        public double? LatencyMedianMs { get; init; }
        public double? LatencyP95Ms { get; init; }
        public DateTime CountingSince { get; init; }
    }

    /// <summary>A model host as the lanes' breaker sees it.</summary>
    public sealed class AiHostStatusModel
    {
        public string Host { get; init; } = string.Empty;
        public bool Up { get; init; }
        public DateTime? DownSince { get; init; }
        public int ConsecutiveFailures { get; init; }
        public string? LastError { get; init; }
        public DateTime? LastProbe { get; init; }
        public IReadOnlyList<string> ModelsAsked { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> ModelsInstalled { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> ModelsMissing { get; init; } = Array.Empty<string>();
    }

    /// <summary>The states <see cref="AiLaneStatusModel.State"/> takes.</summary>
    public static class AiLaneState
    {
        public const string NotStarted = "not-started";
        public const string Disabled = "disabled";
        public const string Running = "running";
        public const string Idle = "idle";
        public const string Paused = "paused";
        public const string QuietHours = "quiet-hours";
        public const string HostDown = "host-down";
        public const string AtCap = "at-cap";
        public const string Failing = "failing";
    }
}

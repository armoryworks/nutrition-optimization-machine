using System;
using System.Text.Encodings.Web;
using System.Text.Json;
using Nom.Data.Audit;

namespace Nom.Orch.Services.Support
{
    /// <summary>
    /// How the lane frame writes to audit.AuditLogEntry. Every row has <see cref="EntityType"/>
    /// LaneState and the state row as its EntityId; the PropertyName is lane/tier. A change of mode
    /// keeps the old mode and reason as OldValue and the new mode, reason and figures as NewValue:
    /// <list type="bullet">
    /// <item><see cref="Qualify"/>: shadow to live, the audited sample reached the bar.</item>
    /// <item><see cref="Pause"/>: live to paused, with the trigger.</item>
    /// <item><see cref="Resume"/>: paused to live, a sample audited since the pause reached the bar again.</item>
    /// <item><see cref="Shadow"/>: live to shadow without a fault: the signature changed, or the golden record or the audit check's score is missing.</item>
    /// <item><see cref="Undo"/>: the lane's undo of its writes since its last clean window, the dry run first and then the apply, with the counts.</item>
    /// </list>
    /// </summary>
    public static class LaneJournal
    {
        public const string EntityType = "LaneState";
        public const string Qualify = "LaneQualify";
        public const string Pause = "LanePause";
        public const string Resume = "LaneResume";
        public const string Shadow = "LaneShadow";
        public const string Undo = "LaneUndo";
        private const int ValueLength = 4000;

        private static readonly JsonSerializerOptions Json = new()
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        };

        /// <summary>A mode as a journal row keeps it.</summary>
        public sealed record ModeValue(string Mode, string? Reason, LaneFigures? Figures = null);

        /// <summary>An undo as a journal row keeps it: dry run or apply, since when, and what it did.</summary>
        public sealed record UndoValue(bool Applied, DateTime SinceUtc, int Undone, int Skipped, int Remaining, string? Note);

        public static string Name(string lane, string tier) => lane + "/" + tier;

        public static AuditLogEntryEntity Entry(long stateId, string lane, string tier, string changeType, string? oldValue, string? newValue, DateTime at, long personId) => new()
        {
            EntityType = EntityType,
            EntityId = stateId,
            ChangeType = changeType,
            PropertyName = Name(lane, tier),
            OldValue = Fit(oldValue),
            NewValue = Fit(newValue),
            Timestamp = at,
            ChangedByPersonId = personId,
        };

        public static string Write<T>(T value) => JsonSerializer.Serialize(value, Json);

        public static T? Read<T>(string? value) where T : class
        {
            if (string.IsNullOrEmpty(value)) return null;
            try
            {
                return JsonSerializer.Deserialize<T>(value, Json);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static string? Fit(string? value) => value == null || value.Length <= ValueLength ? value : value[..ValueLength];
    }
}

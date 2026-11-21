using System;
using Nom.Data.Audit;

namespace Nom.Orch.Services.Support
{
    /// <summary>
    /// The journal of scraping-source state (audit.AuditLogEntry, EntityType <c>ScrapingSource</c>).
    /// Each row names one field (<see cref="StatusField"/>, <see cref="HarvestField"/>,
    /// <see cref="RecipesField"/>, <see cref="VerdictField"/>, <see cref="ProbationField"/>,
    /// <see cref="LicenceField"/>, <see cref="RightsField"/>, <see cref="AiReservedField"/>,
    /// <see cref="PermissionField"/>, <see cref="FactsOnlyField"/>) with its
    /// old and new state, written as <c>state</c> or <c>state | detail</c>. A row of an undoable
    /// field is undone by setting the field back to its old state while it still holds the new one,
    /// journaled as <see cref="Undo"/>; an undo is a row like any other, so it can be undone too.
    /// </summary>
    public static class SourceJournal
    {
        public const string EntityType = "ScrapingSource";

        public const string Approve = "SourceApprove";
        public const string Reject = "SourceReject";
        public const string HandRecord = "SourceHandRecord";
        public const string HarvestPause = "SourceHarvestPause";
        public const string HarvestResume = "SourceHarvestResume";
        public const string Withhold = "SourceWithhold";
        public const string Release = "SourceRelease";
        public const string Recheck = "SourceRecheck";
        public const string ProbationPass = "SourceProbationPass";
        public const string ProbationFail = "SourceProbationFail";
        public const string Undo = "SourceUndo";
        public const string Rights = "SourceRights";
        public const string Permission = "SourcePermission";
        public const string FactsOnlyUse = "SourceFactsOnlyUse";

        public const string StatusField = "Status";
        public const string HarvestField = "Harvest";
        public const string RecipesField = "Recipes";
        public const string VerdictField = "CheckVerdict";
        public const string ProbationField = "Probation";
        public const string LicenceField = "Licence";
        public const string RightsField = "Rights";
        public const string AiReservedField = "AiReserved";
        public const string PermissionField = "Permission";
        public const string FactsOnlyField = "FactsOnlyUse";

        public const string Running = "running";
        public const string Paused = "paused";
        public const string Shown = "shown";
        public const string Withheld = "withheld";
        public const string None = "none";

        private const string Separator = " | ";

        public static bool Undoable(string? field) => field is StatusField or HarvestField or RecipesField or LicenceField or RightsField or AiReservedField or PermissionField or FactsOnlyField;

        public static string Value(string state, string? detail = null) =>
            string.IsNullOrWhiteSpace(detail) ? state : state + Separator + detail.Trim();

        public static string State(string? value)
        {
            if (string.IsNullOrEmpty(value)) return None;
            var at = value.IndexOf(Separator, StringComparison.Ordinal);
            return at < 0 ? value : value[..at];
        }

        public static string? Detail(string? value)
        {
            if (string.IsNullOrEmpty(value)) return null;
            var at = value.IndexOf(Separator, StringComparison.Ordinal);
            return at < 0 ? null : value[(at + Separator.Length)..];
        }

        public static AuditLogEntryEntity Entry(long sourceId, string changeType, string field, string oldValue, string newValue, DateTime at, long personId) => new()
        {
            EntityType = EntityType,
            EntityId = sourceId,
            ChangeType = changeType,
            PropertyName = field,
            OldValue = Clip(oldValue),
            NewValue = Clip(newValue),
            Timestamp = at,
            ChangedByPersonId = personId,
        };

        private static string Clip(string value) => value.Length <= 4000 ? value : value[..4000];
    }
}

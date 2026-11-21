namespace Nom.Orch.Settings
{
    /// <summary>
    /// The source rights engine (section SourceRights; docs/architecture/source-rights.md): the gate on
    /// fetching by a source's rights class, and how long a facts-only recipe's source prose is kept.
    /// </summary>
    public class SourceRightsSettings
    {
        public const string SectionName = "SourceRights";

        /// <summary>
        /// Gates harvesting, URL imports, discovery seeds and the staged import on the rights class: only
        /// clear sources, sources with a recorded permission and facts-only sources an admin approved for
        /// facts-only use are fetched. Off when there is no configuration; on in appsettings.json.
        /// </summary>
        public bool Enforce { get; set; }

        /// <summary>Days a facts-only recipe's rewrite must have stood (or the lane have given it up) before its source prose is reduced.</summary>
        public int ProseRetentionDays { get; set; } = 30;

        /// <summary>Days the reduced source documents are held (soft-deleted) so the reduction can be undone; then they are purged.</summary>
        public int RetentionUndoDays { get; set; } = 30;

        /// <summary>Runs the reduction and the purge with the source lane's tick; off by default.</summary>
        public bool RetentionAuto { get; set; }

        /// <summary>Recipes one reduction run takes.</summary>
        public int RetentionBatch { get; set; } = 500;
    }
}

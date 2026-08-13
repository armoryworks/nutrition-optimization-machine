using Nom.Data.Person;

namespace Nom.Data.Plan
{
    /// <summary>
    /// Per-member feature policy (household-policies design doc §3): feature
    /// gates, frequency caps, and curated-only mode, set by a household
    /// steward or an external management tool. Enforcement is in the
    /// orchestration layer; absence of a row (or of a gate key) means allowed.
    /// </summary>
    public class MemberPolicyEntity : BaseEntity
    {
        public long HouseholdId { get; set; }
        public virtual HouseholdEntity? Household { get; set; }

        public long PersonId { get; set; }
        public virtual PersonEntity? Person { get; set; }

        /// <summary>
        /// jsonb map of gate key → bool; absent key = allowed, explicit false =
        /// gated. Known keys (unknown keys ignored, never errors): "shuffle",
        /// "recipe_import", "recipe_create", "recipe_edit".
        /// </summary>
        public string FeatureGates { get; set; } = "{}";

        /// <summary>jsonb array of {"tag": string, "max_per_week": int}. Shapes shuffle; warns on manual edits.</summary>
        public string FrequencyCaps { get; set; } = "[]";

        /// <summary>Member sees/uses only audience-scoped and steward-approved recipes.</summary>
        public bool CuratedOnly { get; set; }

        /// <summary>Opaque marker of who manages this policy ("person:123" steward or "brigade:456").</summary>
        public string? UpdatedBy { get; set; }
    }
}

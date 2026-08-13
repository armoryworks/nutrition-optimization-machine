using System.Collections.Generic;

namespace Nom.Orch.Models.Policy
{
    /// <summary>A per-week frequency cap on recipes carrying a tag.</summary>
    public class FrequencyCapModel
    {
        public string Tag { get; set; } = string.Empty;
        public int MaxPerWeek { get; set; }
    }

    /// <summary>
    /// Per-member policy as exchanged with the API (jsonb columns are
    /// serialized/deserialized at the orchestration layer).
    /// </summary>
    public class MemberPolicyModel
    {
        public long HouseholdId { get; set; }
        public long PersonId { get; set; }

        /// <summary>Gate key → allowed?; absent key = allowed. Known keys: shuffle, recipe_import, recipe_create, recipe_edit.</summary>
        public Dictionary<string, bool> FeatureGates { get; set; } = new();

        public List<FrequencyCapModel> FrequencyCaps { get; set; } = new();

        public bool CuratedOnly { get; set; }

        /// <summary>Opaque manager marker of the last writer ("person:123" or an external tool id).</summary>
        public string? UpdatedBy { get; set; }
    }

    /// <summary>Steward-created restriction for a member, optionally locked.</summary>
    public class StewardRestrictionRequestModel
    {
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public long? RestrictionTypeId { get; set; }
        public long? IngredientId { get; set; }
        public long? NutrientId { get; set; }
        public int? Severity { get; set; } = 5;
        public bool Locked { get; set; } = true;
    }
}

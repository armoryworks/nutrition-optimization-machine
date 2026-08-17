namespace Nom.Data.Platform
{
    /// <summary>
    /// A platform-wide on/off switch an operator controls from the admin UI —
    /// distinct from <see cref="Plan.MemberPolicyEntity.FeatureGates"/>, which
    /// gates features for one person within a household.
    ///
    /// Used to ship a subsystem dark: the code deploys, the switch stays off,
    /// and it is turned on deliberately (e.g. once its legal review clears)
    /// rather than by a release landing.
    /// </summary>
    public class PlatformFeatureEntity : BaseEntity
    {
        /// <summary>Stable key referenced in code, e.g. "brigade".</summary>
        public string Key { get; set; } = string.Empty;

        public bool IsEnabled { get; set; }

        /// <summary>What turning this on actually does — shown in the admin UI.</summary>
        public string? Description { get; set; }
    }
}

// File: Nom.Orch/Models/Household/HouseholdEnrollmentInfoModel.cs

namespace Nom.Orch.Models.Household
{
    /// <summary>
    /// External-management enrollment info for a household — the client-side
    /// bridge the consent UI needs before talking to the management tool.
    /// </summary>
    public class HouseholdEnrollmentInfoModel
    {
        /// <summary>
        /// Opaque external-management marker (e.g. "brigade:123"), or null
        /// when the household is self-managed.
        /// </summary>
        public string? ManagedBy { get; set; }

        /// <summary>
        /// Human-readable provider name for consent screens.
        /// TODO: Brigade owns provider identity; populate once a provider
        /// directory lookup exists. Null for now.
        /// </summary>
        public string? ProviderDisplayName { get; set; }
    }
}

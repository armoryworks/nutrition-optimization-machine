// File: Nom.Data/Reference/PrivacyConsentTypeViewEntity.cs

namespace Nom.Data.Reference
{
    /// <summary>
    /// Represents a grouped reference view entity specifically for Privacy Consent Types.
    /// This is materialized by Entity Framework Core when the GroupId in the
    /// reference.ReferenceGroupView matches the PrivacyConsentType Group's ID.
    /// </summary>
    public class PrivacyConsentTypeViewEntity : GroupedReferenceViewEntity
    {
        // This class inherits all necessary properties from GroupedReferenceViewEntity.
        // No additional properties are needed as its purpose is to act as a discriminator
        // in the Table-Per-Hierarchy (TPH) strategy for reference data views.
    }
}

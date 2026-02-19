namespace Nom.Data.Reference
{
    /// <summary>
    /// Represents a grouped reference view entity specifically for Personal Preference Types.
    /// Materialized by EF Core when GroupId matches the PersonalPreferenceType Group's ID in the view.
    /// </summary>
    public class PersonalPreferenceTypeViewEntity : GroupedReferenceViewEntity
    {
        // Inherits properties from GroupedReferenceViewEntity
    }
}

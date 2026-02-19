namespace Nom.Data.Reference
{
    /// <summary>
    /// Represents a grouped reference view entity specifically for Societal Restriction Types.
    /// Materialized by EF Core when GroupId matches the SocietalRestrictionType Group's ID in the view.
    /// </summary>
    public class SocietalRestrictionTypeViewEntity : GroupedReferenceViewEntity
    {
        // Inherits properties from GroupedReferenceViewEntity
    }
}

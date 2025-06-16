// Nom.Data.Reference/RestrictionTypeViewEntity.cs
namespace Nom.Data.Reference
{
    /// <summary>
    /// Represents a grouped reference view entity specifically for Restriction Types.
    /// Materialized by EF Core when GroupId matches the RestrictionType Group's ID in the view.
    /// </summary>
    public class RestrictionTypeViewEntity : GroupedReferenceViewEntity
    {
        // Inherits properties from GroupedReferenceViewEntity
    }
}
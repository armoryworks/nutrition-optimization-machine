// Nom.Data.Reference/PersonAttributeTypeViewEntity.cs

namespace Nom.Data.Reference
{
    /// <summary>
    /// Represents a grouped reference view entity specifically for Person Attribute Types.
    /// Materialized by EF Core when GroupId matches the PersonAttributeType Group's ID in the view.
    /// </summary>
    public class PersonAttributeTypeViewEntity : GroupedReferenceViewEntity
    {
        // Inherits properties from GroupedReferenceViewEntity
    }
}

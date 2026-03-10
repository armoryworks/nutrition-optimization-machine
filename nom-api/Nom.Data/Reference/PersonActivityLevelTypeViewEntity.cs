// Nom.Data.Reference/PersonActivityLevelTypeViewEntity.cs

namespace Nom.Data.Reference
{
    /// <summary>
    /// Represents a grouped reference view entity specifically for Person Activity Level Types.
    /// Materialized by EF Core when GroupId matches the PersonActivityLevelType Group's ID in the view.
    /// </summary>
    public class PersonActivityLevelTypeViewEntity : GroupedReferenceViewEntity
    {
        // Inherits properties from GroupedReferenceViewEntity
    }
}

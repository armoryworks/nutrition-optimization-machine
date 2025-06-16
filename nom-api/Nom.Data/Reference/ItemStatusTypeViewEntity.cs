// Nom.Data.Reference/ItemStatusTypeViewEntity.cs
namespace Nom.Data.Reference
{
    /// <summary>
    /// Represents a grouped reference view entity specifically for Pantry Item Status Types.
    /// Materialized by EF Core when GroupId matches the ItemStatusType Group's ID in the view.
    /// </summary>
    public class ItemStatusTypeViewEntity : GroupedReferenceViewEntity
    {
        // Inherits properties from GroupedReferenceViewEntity
    }
}
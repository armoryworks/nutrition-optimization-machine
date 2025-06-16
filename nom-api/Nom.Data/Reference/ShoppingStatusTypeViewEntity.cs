// Nom.Data.Reference/ShoppingStatusTypeViewEntity.cs
namespace Nom.Data.Reference
{
    /// <summary>
    /// Represents a grouped reference view entity specifically for Shopping Status Types.
    /// Materialized by EF Core when GroupId matches the ShoppingStatusType Group's ID in the view.
    /// </summary>
    public class ShoppingStatusTypeViewEntity : GroupedReferenceViewEntity
    {
        // Inherits properties from GroupedReferenceViewEntity
    }
}
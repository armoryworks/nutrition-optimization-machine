// Nom.Data.Reference/CuisineTypeViewEntity.cs
namespace Nom.Data.Reference
{
    /// <summary>
    /// Represents a grouped reference view entity specifically for Cuisine Types.
    /// Materialized by EF Core when GroupId matches the CuisineType Group's ID in the view.
    /// </summary>
    public class CuisineTypeViewEntity : GroupedReferenceViewEntity
    {
        // Inherits properties from GroupedReferenceViewEntity
    }
}
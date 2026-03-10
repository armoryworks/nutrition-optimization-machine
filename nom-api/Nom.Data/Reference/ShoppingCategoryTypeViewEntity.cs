// Nom.Data.Reference/ShoppingCategoryTypeViewEntity.cs

namespace Nom.Data.Reference
{
    /// <summary>
    /// Represents a grouped reference view entity specifically for Shopping Category Types.
    /// Materialized by EF Core when GroupId matches the ShoppingCategoryType Group's ID in the view.
    /// </summary>
    public class ShoppingCategoryTypeViewEntity : GroupedReferenceViewEntity
    {
        // Inherits properties from GroupedReferenceViewEntity
    }
}

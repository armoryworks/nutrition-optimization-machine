// Nom.Data.Reference/ShoppingPriorityTypeViewEntity.cs
using System.ComponentModel.DataAnnotations.Schema;

namespace Nom.Data.Reference
{
    /// <summary>
    /// Represents a grouped reference view entity specifically for Shopping Priority Types.
    /// Materialized by EF Core when GroupId matches the ShoppingPriorityType Group's ID in the view.
    /// </summary>
    public class ShoppingPriorityTypeViewEntity : GroupedReferenceViewEntity
    {
        // Inherits properties from GroupedReferenceViewEntity
    }
}

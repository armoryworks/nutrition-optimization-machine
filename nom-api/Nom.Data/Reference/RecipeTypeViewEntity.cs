// Nom.Data.Reference/RecipeTypeViewEntity.cs
namespace Nom.Data.Reference
{
    /// <summary>
    /// Represents a grouped reference view entity specifically for Recipe Types.
    /// Materialized by EF Core when GroupId matches the RecipeType Group's ID in the view.
    /// </summary>
    public class RecipeTypeViewEntity : GroupedReferenceViewEntity
    {
        // Inherits properties from GroupedReferenceViewEntity
    }
}
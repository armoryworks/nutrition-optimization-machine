// Nom.Data.Reference/MealTypeViewEntity.cs

namespace Nom.Data.Reference
{
    /// <summary>
    /// Represents a grouped reference view entity specifically for Meal Types.
    /// Materialized by EF Core when GroupId matches the Meal Type Group's ID in the view.
    /// </summary>
    public class MealTypeViewEntity : GroupedReferenceViewEntity
    {
        // No new properties typically needed here.
    }
}
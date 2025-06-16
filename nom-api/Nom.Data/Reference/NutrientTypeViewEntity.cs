// Nom.Data.Reference/NutrientTypeViewEntity.cs
namespace Nom.Data.Reference
{
    /// <summary>
    /// Represents a grouped reference view entity specifically for Nutrient Types.
    /// Materialized by EF Core when GroupId matches the NutrientType Group's ID in the view.
    /// </summary>
    public class NutrientTypeViewEntity : GroupedReferenceViewEntity
    {
        // Inherits properties from GroupedReferenceViewEntity
    }
}
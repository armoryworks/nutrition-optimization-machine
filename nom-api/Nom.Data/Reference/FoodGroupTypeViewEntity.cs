// Nom.Data.Reference/FoodGroupTypeViewEntity.cs

namespace Nom.Data.Reference
{
    /// <summary>
    /// Grouped reference view for nutritional Food Group Types (Vegetables, Fruits,
    /// Grains, Protein Foods, Dairy, and common extensions). Materialized by EF Core
    /// when GroupId matches the FoodGroupType Group's ID in the ReferenceGroupView.
    /// </summary>
    public class FoodGroupTypeViewEntity : GroupedReferenceViewEntity
    {
        // Inherits properties from GroupedReferenceViewEntity
    }
}

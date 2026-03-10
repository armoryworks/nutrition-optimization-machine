// Nom.Data.Reference/RecipeDifficultyTypeViewEntity.cs

namespace Nom.Data.Reference
{
    /// <summary>
    /// Represents a grouped reference view entity specifically for Recipe Difficulty Types.
    /// Materialized by EF Core when GroupId matches the RecipeDifficultyType Group's ID in the view.
    /// </summary>
    public class RecipeDifficultyTypeViewEntity : GroupedReferenceViewEntity
    {
        // Inherits properties from GroupedReferenceViewEntity
    }
}

// Nom.Data.Reference/GoalTypeViewEntity.cs
namespace Nom.Data.Reference
{
    /// <summary>
    /// Represents a grouped reference view entity specifically for Goal Types.
    /// Materialized by EF Core when GroupId matches the GoalType Group's ID in the view.
    /// </summary>
    public class GoalTypeViewEntity : GroupedReferenceViewEntity
    {
        // Inherits properties from GroupedReferenceViewEntity
    }
}
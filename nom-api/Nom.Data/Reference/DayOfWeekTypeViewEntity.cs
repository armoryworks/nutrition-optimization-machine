// Nom.Data.Reference/DayOfWeekTypeViewEntity.cs

namespace Nom.Data.Reference
{
    /// <summary>
    /// Represents a grouped reference view entity specifically for Day of Week Types.
    /// Materialized by EF Core when GroupId matches the DayOfWeekType Group's ID in the view.
    /// </summary>
    public class DayOfWeekTypeViewEntity : GroupedReferenceViewEntity
    {
        // Inherits properties from GroupedReferenceViewEntity
    }
}

// Nom.Data.Reference/PersonHealthGoalTypeViewEntity.cs
using System.ComponentModel.DataAnnotations.Schema;

namespace Nom.Data.Reference
{
    /// <summary>
    /// Represents a grouped reference view entity specifically for Person Health Goal Types.
    /// Materialized by EF Core when GroupId matches the PersonHealthGoalType Group's ID in the view.
    /// </summary>
    public class PersonHealthGoalTypeViewEntity : GroupedReferenceViewEntity
    {
        // Inherits properties from GroupedReferenceViewEntity
    }
}

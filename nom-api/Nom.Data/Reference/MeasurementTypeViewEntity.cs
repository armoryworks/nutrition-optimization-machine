// Nom.Data.Reference/MeasurementTypeViewEntity.cs
using System.ComponentModel.DataAnnotations.Schema;

namespace Nom.Data.Reference
{
    /// <summary>
    /// Represents a grouped reference view entity specifically for Measurement Type units.
    /// Materialized by EF Core when GroupId matches the Measurement Group's ID in the view.
    /// </summary>
    public class MeasurementTypeViewEntity : GroupedReferenceViewEntity
    {
        // No new properties typically needed here, inherits all from base view class.
    }
}
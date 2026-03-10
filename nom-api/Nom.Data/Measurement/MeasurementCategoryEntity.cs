using System.Collections.Generic;
using Nom.Data.Audit;

namespace Nom.Data.Measurement
{
    /// <summary>
    /// Represents a category or group for measurement units (e.g., "Mass", "Volume", "Count").
    /// Maps to the 'measurement.MeasurementCategory' table.
    /// </summary>
    public class MeasurementCategoryEntity : BaseEntity
    {
        public required string Name { get; set; }

        public string? Description { get; set; }

        public long? BaseUnitId { get; set; }

        public virtual BaseMeasurementEntity? BaseUnit { get; set; }

        /// <summary>
        /// Navigation property to a collection of MeasurementEntity instances
        /// that belong to this category.
        /// </summary>
        public virtual ICollection<MeasurementEntity> Measurements { get; set; } = new List<MeasurementEntity>();
    }
}

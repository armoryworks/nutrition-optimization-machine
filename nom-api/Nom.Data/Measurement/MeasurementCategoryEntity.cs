using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Nom.Data.Audit;

namespace Nom.Data.Measurement
{
    /// <summary>
    /// Represents a category or group for measurement units (e.g., "Mass", "Volume", "Count").
    /// Maps to the 'measurement.MeasurementCategory' table.
    /// </summary>
    [Table("MeasurementCategory", Schema = "measurement")]
    public class MeasurementCategoryEntity : BaseEntity
    {
        [Required]
        [MaxLength(100)]
        public required string Name { get; set; }

        [MaxLength(500)]
        public string? Description { get; set; }

        public long? BaseUnitId { get; set; }

        [ForeignKey(nameof(BaseUnitId))]
        public virtual BaseMeasurementEntity? BaseUnit { get; set; }

        /// <summary>
        /// Navigation property to a collection of MeasurementEntity instances
        /// that belong to this category.
        /// </summary>
        public virtual ICollection<MeasurementEntity> Measurements { get; set; } = new List<MeasurementEntity>();
    }
}

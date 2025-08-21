using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Nom.Data.Audit;

namespace Nom.Data.Measurement
{
    /// <summary>
    /// Concrete base class for basic measurements that can be instantiated directly.
    /// Maps to the 'measurement.Measurement' table.
    /// </summary>
    [Table("Measurement", Schema = "measurement")]
    public class BaseMeasurementEntity : MeasurementEntity
    {
        // Inherits all properties from MeasurementEntity
        // This concrete class allows direct instantiation for basic measurements
    }
}

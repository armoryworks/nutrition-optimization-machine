using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Nom.Data.Audit;

namespace Nom.Data.Measurement
{
    /// <summary>
    /// Concrete implementation of MeasurementEntity for basic measurement units.
    /// </summary>
    [Table("Measurement", Schema = "measurement")]
    public class BasicMeasurementEntity : MeasurementEntity
    {
        // Additional properties specific to basic measurements can be added here
        // For now, this is a simple concrete implementation
    }
}


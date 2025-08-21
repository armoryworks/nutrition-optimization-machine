using System.ComponentModel.DataAnnotations;

namespace Nom.Orch.Models.Measurement
{
    /// <summary>
    /// Request model for updating an existing measurement.
    /// </summary>
    public class UpdateMeasurementRequest
    {
        [MaxLength(100)]
        public string? Name { get; set; }

        [MaxLength(500)]
        public string? Description { get; set; }

        [MaxLength(20)]
        public string? Symbol { get; set; }

        public long? CategoryId { get; set; }

        public bool? IsBaseUnit { get; set; }

        public decimal? BaseUnitConversionFactor { get; set; }
    }
}

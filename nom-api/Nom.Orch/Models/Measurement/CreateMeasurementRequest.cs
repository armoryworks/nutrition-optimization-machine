using System.ComponentModel.DataAnnotations;

namespace Nom.Orch.Models.Measurement
{
    /// <summary>
    /// Request model for creating a new measurement.
    /// </summary>
    public class CreateMeasurementRequest
    {
        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(500)]
        public string? Description { get; set; }

        [Required]
        [MaxLength(20)]
        public string Symbol { get; set; } = string.Empty;

        [Required]
        public long CategoryId { get; set; }

        public bool IsBaseUnit { get; set; } = false;

        public decimal? BaseUnitConversionFactor { get; set; }
    }
}

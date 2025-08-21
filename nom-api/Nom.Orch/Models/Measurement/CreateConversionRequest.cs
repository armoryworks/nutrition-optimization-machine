using System.ComponentModel.DataAnnotations;

namespace Nom.Orch.Models.Measurement
{
    /// <summary>
    /// Request model for creating a new measurement conversion rule.
    /// </summary>
    public class CreateConversionRequest
    {
        [Required]
        public long FromMeasurementId { get; set; }

        [Required]
        public long ToMeasurementId { get; set; }

        [Required]
        public decimal ConversionFactor { get; set; }

        public decimal? Offset { get; set; }

        [MaxLength(100)]
        public string? Formula { get; set; }

        public bool IsDirectConversion { get; set; } = true;
    }
}

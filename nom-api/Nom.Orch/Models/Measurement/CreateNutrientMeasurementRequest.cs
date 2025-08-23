using System.ComponentModel.DataAnnotations;

namespace Nom.Orch.Models.Measurement
{
    /// <summary>
    /// Request model for creating nutrient-specific measurements.
    /// </summary>
    public class CreateNutrientMeasurementRequest
    {
        [Required]
        public long NutrientId { get; set; }
        
        [Required]
        public long StandardMeasurementId { get; set; }
        
        public long? DefaultMeasurementId { get; set; }
        
        public decimal? StandardDailyValue { get; set; }
        
        public string? StandardDailyValueUnit { get; set; }
        
        public string? Notes { get; set; }
    }
}




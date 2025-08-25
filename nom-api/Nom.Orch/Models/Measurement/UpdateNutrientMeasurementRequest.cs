using System.ComponentModel.DataAnnotations;

namespace Nom.Orch.Models.Measurement
{
    /// <summary>
    /// Request model for updating nutrient-specific measurements.
    /// </summary>
    public class UpdateNutrientMeasurementRequest
    {
        [Required]
        public long Id { get; set; }
        
        [Required]
        public long StandardMeasurementId { get; set; }
        
        public long? DefaultMeasurementId { get; set; }
        
        public decimal? StandardDailyValue { get; set; }
        
        public string? StandardDailyValueUnit { get; set; }
        
        public string? Notes { get; set; }
    }
}







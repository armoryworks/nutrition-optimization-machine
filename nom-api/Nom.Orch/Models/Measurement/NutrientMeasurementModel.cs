using System.ComponentModel.DataAnnotations;

namespace Nom.Orch.Models.Measurement
{
    /// <summary>
    /// Model for nutrient-specific measurement standards and defaults.
    /// </summary>
    public class NutrientMeasurementModel
    {
        public long Id { get; set; }
        
        [Required]
        public long NutrientId { get; set; }
        
        [Required]
        public string NutrientName { get; set; } = string.Empty;
        
        [Required]
        public long StandardMeasurementId { get; set; }
        
        [Required]
        public string StandardMeasurementName { get; set; } = string.Empty;
        
        [Required]
        public string StandardMeasurementSymbol { get; set; } = string.Empty;
        
        public long? DefaultMeasurementId { get; set; }
        
        public string? DefaultMeasurementName { get; set; }
        
        public string? DefaultMeasurementSymbol { get; set; }
        
        public decimal? StandardDailyValue { get; set; }
        
        public string? StandardDailyValueUnit { get; set; }
        
        public string? Notes { get; set; }
        
        public DateTime CreatedDate { get; set; }
        
        public DateTime? LastModifiedDate { get; set; }
    }
}

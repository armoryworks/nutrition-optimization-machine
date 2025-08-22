using System.ComponentModel.DataAnnotations;

namespace Nom.Orch.Models.Measurement
{
    /// <summary>
    /// Model for ingredient-specific measurement preferences and defaults.
    /// </summary>
    public class IngredientMeasurementModel
    {
        public long Id { get; set; }
        
        [Required]
        public long IngredientId { get; set; }
        
        [Required]
        public string IngredientName { get; set; } = string.Empty;
        
        [Required]
        public long PreferredMeasurementId { get; set; }
        
        [Required]
        public string PreferredMeasurementName { get; set; } = string.Empty;
        
        [Required]
        public string PreferredMeasurementSymbol { get; set; } = string.Empty;
        
        public long? DefaultMeasurementId { get; set; }
        
        public string? DefaultMeasurementName { get; set; }
        
        public string? DefaultMeasurementSymbol { get; set; }
        
        public bool IsPreferred { get; set; }
        
        public string? Notes { get; set; }
        
        public DateTime CreatedDate { get; set; }
        
        public DateTime? LastModifiedDate { get; set; }
    }
}

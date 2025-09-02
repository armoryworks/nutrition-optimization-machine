using System.ComponentModel.DataAnnotations;

namespace Nom.Orch.Models.Measurement
{
    /// <summary>
    /// Request model for creating ingredient-specific measurements.
    /// </summary>
    public class CreateIngredientMeasurementRequest
    {
        [Required]
        public long IngredientId { get; set; }
        
        [Required]
        public long PreferredMeasurementId { get; set; }
        
        public long? DefaultMeasurementId { get; set; }
        
        public bool IsPreferred { get; set; } = true;
        
        public string? Notes { get; set; }
    }
}










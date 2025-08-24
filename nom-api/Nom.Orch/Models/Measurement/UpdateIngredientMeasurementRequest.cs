using System.ComponentModel.DataAnnotations;

namespace Nom.Orch.Models.Measurement
{
    /// <summary>
    /// Request model for updating ingredient-specific measurements.
    /// </summary>
    public class UpdateIngredientMeasurementRequest
    {
        [Required]
        public long Id { get; set; }
        
        [Required]
        public long PreferredMeasurementId { get; set; }
        
        public long? DefaultMeasurementId { get; set; }
        
        public bool IsPreferred { get; set; }
        
        public string? Notes { get; set; }
    }
}





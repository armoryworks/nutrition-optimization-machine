using System.ComponentModel.DataAnnotations;

namespace Nom.Orch.Models.Measurement
{
    /// <summary>
    /// Request model for creating a new measurement category.
    /// </summary>
    public class CreateCategoryRequest
    {
        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(500)]
        public string? Description { get; set; }

        [Required]
        public long BaseUnitId { get; set; }
    }
}

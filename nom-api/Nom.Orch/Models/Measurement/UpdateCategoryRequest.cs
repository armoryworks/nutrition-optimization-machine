using System.ComponentModel.DataAnnotations;

namespace Nom.Orch.Models.Measurement
{
    /// <summary>
    /// Request model for updating an existing measurement category.
    /// </summary>
    public class UpdateCategoryRequest
    {
        [Required]
        public long Id { get; set; }

        [MaxLength(100)]
        public string? Name { get; set; }

        [MaxLength(500)]
        public string? Description { get; set; }

        public long? BaseUnitId { get; set; }
    }
}

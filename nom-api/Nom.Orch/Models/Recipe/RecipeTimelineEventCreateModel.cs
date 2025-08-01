using System.ComponentModel.DataAnnotations;

namespace Nom.Orch.Models.Recipe
{
    public class RecipeTimelineEventCreateModel
    {
        [Required]
        public long RecipeId { get; set; }
        
        [Required]
        public long EventTypeId { get; set; }
        
        [Required]
        [StringLength(255, ErrorMessage = "Event title cannot exceed 255 characters.")]
        public required string EventTitle { get; set; }
        
        [StringLength(2047, ErrorMessage = "Event description cannot exceed 2047 characters.")]
        public string? EventDescription { get; set; }
        
        [Required]
        public DateTime EventDate { get; set; } = DateTime.UtcNow;
    }
} 
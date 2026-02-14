using System.ComponentModel.DataAnnotations;

namespace Nom.Orch.Models.Communication
{
    public class CreateThreadRequest
    {
        [Required]
        public required long[] ParticipantIds { get; set; }

        public int ThreadType { get; set; } = 0;
        public long? RecipeId { get; set; }
        public long? IngredientId { get; set; }
        public long? PlanId { get; set; }
    }
}

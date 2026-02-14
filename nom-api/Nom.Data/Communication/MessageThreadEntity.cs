using Nom.Data.Plan;
using Nom.Data.Recipe;
using System.ComponentModel.DataAnnotations.Schema;

namespace Nom.Data.Communication
{
    [Table("MessageThreads")]
    public class MessageThreadEntity : BaseEntity
    {
        public int ThreadType { get; set; } = 0; // 0=Normal, 1=CurationFeedback

        // Contextual links (nullable)
        public long? RecipeId { get; set; }
        [ForeignKey("RecipeId")]
        public virtual RecipeEntity? Recipe { get; set; }

        public long? IngredientId { get; set; }
        [ForeignKey("IngredientId")]
        public virtual IngredientEntity? Ingredient { get; set; }

        public long? PlanId { get; set; }
        [ForeignKey("PlanId")]
        public virtual PlanEntity? Plan { get; set; }

        public virtual ICollection<MessageThreadParticipantEntity> Participants { get; set; } = new List<MessageThreadParticipantEntity>();
        public virtual ICollection<MessageEntity> Messages { get; set; } = new List<MessageEntity>();

    }
}
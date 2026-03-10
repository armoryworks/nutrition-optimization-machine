using Nom.Data.Plan;
using Nom.Data.Recipe;

namespace Nom.Data.Communication
{
    public class MessageThreadEntity : BaseEntity
    {
        public int ThreadType { get; set; } = 0; // 0=Normal, 1=CurationFeedback

        // Contextual links (nullable)
        public long? RecipeId { get; set; }
        public virtual RecipeEntity? Recipe { get; set; }

        public long? IngredientId { get; set; }
        public virtual IngredientEntity? Ingredient { get; set; }

        public long? PlanId { get; set; }
        public virtual PlanEntity? Plan { get; set; }

        public virtual ICollection<MessageThreadParticipantEntity> Participants { get; set; } = new List<MessageThreadParticipantEntity>();
        public virtual ICollection<MessageEntity> Messages { get; set; } = new List<MessageEntity>();

    }
}
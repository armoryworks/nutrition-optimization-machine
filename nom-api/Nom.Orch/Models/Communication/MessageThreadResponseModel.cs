namespace Nom.Orch.Models.Communication
{
    public class MessageThreadResponseModel
    {
        public long Id { get; set; }
        public List<MessageParticipantResponseModel> Participants { get; set; } = new();
        public MessageResponseModel? LastMessage { get; set; }
        public int UnreadCount { get; set; }
        public DateTime? LastActivity { get; set; }
        public bool IsArchived { get; set; }
        public bool IsPinned { get; set; }
        public int ThreadType { get; set; }
        public long? RecipeId { get; set; }
        public long? IngredientId { get; set; }
        public long? PlanId { get; set; }
    }
}

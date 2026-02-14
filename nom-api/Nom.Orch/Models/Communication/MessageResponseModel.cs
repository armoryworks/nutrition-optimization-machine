namespace Nom.Orch.Models.Communication
{
    public class MessageResponseModel
    {
        public long Id { get; set; }
        public long MessageThreadId { get; set; }
        public long SenderPersonId { get; set; }
        public string SenderDisplayName { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public bool IsRead { get; set; }
    }
}

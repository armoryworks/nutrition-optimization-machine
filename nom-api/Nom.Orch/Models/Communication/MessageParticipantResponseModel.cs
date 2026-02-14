namespace Nom.Orch.Models.Communication
{
    public class MessageParticipantResponseModel
    {
        public long Id { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? AvatarUrl { get; set; }
        public bool IsOnline { get; set; }
        public DateTime? LastSeen { get; set; }
    }
}

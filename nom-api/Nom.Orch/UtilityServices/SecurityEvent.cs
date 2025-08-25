using System;

namespace Nom.Orch.UtilityServices
{
    public class SecurityEvent
    {
        public Guid Id { get; set; }
        public SecurityEventType EventType { get; set; }
        public string Description { get; set; } = string.Empty;
        public string? UserId { get; set; }
        public string? IpAddress { get; set; }
        public DateTime Timestamp { get; set; }
        public SecurityEventSeverity Severity { get; set; }
    }
}




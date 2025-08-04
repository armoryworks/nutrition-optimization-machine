namespace Nom.Api.Events
{
    /// <summary>
    /// Base implementation for events
    /// </summary>
    public abstract class BaseEvent : IEvent
    {
        public Guid EventId { get; } = Guid.NewGuid();
        public DateTime Timestamp { get; } = DateTime.UtcNow;
        public abstract string EventType { get; }
        public string Source { get; set; } = "Unknown";
        public string? CorrelationId { get; set; }

        protected BaseEvent()
        {
        }

        protected BaseEvent(string source, string? correlationId = null)
        {
            Source = source;
            CorrelationId = correlationId;
        }
    }
}
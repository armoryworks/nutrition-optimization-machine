using System;

namespace Nom.Api.Events
{
    /// <summary>
    /// Base interface for all events in the application
    /// </summary>
    public interface IEvent
    {
        /// <summary>
        /// Gets the event ID
        /// </summary>
        Guid EventId { get; }

        /// <summary>
        /// Gets the timestamp when the event was created
        /// </summary>
        DateTime Timestamp { get; }

        /// <summary>
        /// Gets the event type name
        /// </summary>
        string EventType { get; }

        /// <summary>
        /// Gets the source of the event
        /// </summary>
        string Source { get; }

        /// <summary>
        /// Gets the correlation ID for tracking related events
        /// </summary>
        string? CorrelationId { get; }
    }
}




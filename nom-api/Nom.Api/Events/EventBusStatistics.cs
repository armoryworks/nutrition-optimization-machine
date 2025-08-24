using System;

namespace Nom.Api.Events
{
    /// <summary>
    /// Event bus statistics
    /// </summary>
    public class EventBusStatistics
    {
        /// <summary>
        /// Total number of events published
        /// </summary>
        public long TotalEventsPublished { get; set; }

        /// <summary>
        /// Total number of events handled
        /// </summary>
        public long TotalEventsHandled { get; set; }

        /// <summary>
        /// Total number of event handler errors
        /// </summary>
        public long TotalHandlerErrors { get; set; }

        /// <summary>
        /// Average event processing time in milliseconds
        /// </summary>
        public double AverageProcessingTimeMs { get; set; }

        /// <summary>
        /// Number of active subscribers
        /// </summary>
        public int ActiveSubscribers { get; set; }

        /// <summary>
        /// Number of event types registered
        /// </summary>
        public int RegisteredEventTypes { get; set; }

        /// <summary>
        /// Timestamp of the last event published
        /// </summary>
        public DateTime? LastEventPublished { get; set; }

        /// <summary>
        /// Timestamp of the last event handled
        /// </summary>
        public DateTime? LastEventHandled { get; set; }
    }
}


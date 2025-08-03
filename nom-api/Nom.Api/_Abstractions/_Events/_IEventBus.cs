// File: Nom.Api/_Abstractions/_Events/_IEventBus.cs

using System;
using System.Threading.Tasks;

namespace Nom.Api._Abstractions._Events
{
    /// <summary>
    /// Base interface for all events in the application
    /// </summary>
    public interface _IEvent
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

    /// <summary>
    /// Base implementation for events
    /// </summary>
    public abstract class _BaseEvent : _IEvent
    {
        public Guid EventId { get; } = Guid.NewGuid();
        public DateTime Timestamp { get; } = DateTime.UtcNow;
        public abstract string EventType { get; }
        public string Source { get; set; } = "Unknown";
        public string? CorrelationId { get; set; }

        protected _BaseEvent()
        {
        }

        protected _BaseEvent(string source, string? correlationId = null)
        {
            Source = source;
            CorrelationId = correlationId;
        }
    }

    /// <summary>
    /// Interface for event handlers
    /// </summary>
    /// <typeparam name="TEvent">The type of event to handle</typeparam>
    public interface _IEventHandler<in TEvent> where TEvent : _IEvent
    {
        /// <summary>
        /// Handles the event
        /// </summary>
        /// <param name="event">The event to handle</param>
        /// <returns>Task representing the async operation</returns>
        Task HandleAsync(TEvent @event);

        /// <summary>
        /// Gets the priority of this handler (lower numbers = higher priority)
        /// </summary>
        int Priority { get; }

        /// <summary>
        /// Gets whether this handler should be executed asynchronously
        /// </summary>
        bool IsAsync { get; }
    }

    /// <summary>
    /// Base implementation for event handlers
    /// </summary>
    /// <typeparam name="TEvent">The type of event to handle</typeparam>
    public abstract class _BaseEventHandler<TEvent> : _IEventHandler<TEvent> where TEvent : _IEvent
    {
        public virtual int Priority => 0;
        public virtual bool IsAsync => true;

        public abstract Task HandleAsync(TEvent @event);
    }

    /// <summary>
    /// Interface for the event bus
    /// </summary>
    public interface _IEventBus
    {
        /// <summary>
        /// Publishes an event to all registered handlers
        /// </summary>
        /// <typeparam name="TEvent">The type of event to publish</typeparam>
        /// <param name="event">The event to publish</param>
        /// <returns>Task representing the async operation</returns>
        Task PublishAsync<TEvent>(TEvent @event) where TEvent : _IEvent;

        /// <summary>
        /// Subscribes to events of the specified type
        /// </summary>
        /// <typeparam name="TEvent">The type of event to subscribe to</typeparam>
        /// <param name="handler">The event handler</param>
        void Subscribe<TEvent>(_IEventHandler<TEvent> handler) where TEvent : _IEvent;

        /// <summary>
        /// Unsubscribes from events of the specified type
        /// </summary>
        /// <typeparam name="TEvent">The type of event to unsubscribe from</typeparam>
        /// <param name="handler">The event handler to unsubscribe</param>
        void Unsubscribe<TEvent>(_IEventHandler<TEvent> handler) where TEvent : _IEvent;

        /// <summary>
        /// Subscribes to events using a delegate
        /// </summary>
        /// <typeparam name="TEvent">The type of event to subscribe to</typeparam>
        /// <param name="handler">The delegate to handle the event</param>
        /// <param name="priority">The priority of the handler</param>
        void Subscribe<TEvent>(Func<TEvent, Task> handler, int priority = 0) where TEvent : _IEvent;

        /// <summary>
        /// Unsubscribes from events using a delegate
        /// </summary>
        /// <typeparam name="TEvent">The type of event to unsubscribe from</typeparam>
        /// <param name="handler">The delegate to unsubscribe</param>
        void Unsubscribe<TEvent>(Func<TEvent, Task> handler) where TEvent : _IEvent;

        /// <summary>
        /// Gets the number of subscribers for a specific event type
        /// </summary>
        /// <typeparam name="TEvent">The type of event</typeparam>
        /// <returns>The number of subscribers</returns>
        int GetSubscriberCount<TEvent>() where TEvent : _IEvent;

        /// <summary>
        /// Clears all subscribers for a specific event type
        /// </summary>
        /// <typeparam name="TEvent">The type of event</typeparam>
        void ClearSubscribers<TEvent>() where TEvent : _IEvent;

        /// <summary>
        /// Clears all subscribers for all event types
        /// </summary>
        void ClearAllSubscribers();

        /// <summary>
        /// Gets whether the event bus is enabled
        /// </summary>
        bool IsEnabled { get; }

        /// <summary>
        /// Enables or disables the event bus
        /// </summary>
        /// <param name="enabled">Whether to enable the event bus</param>
        void SetEnabled(bool enabled);
    }

    /// <summary>
    /// Configuration options for the event bus
    /// </summary>
    public class _EventBusOptions
    {
        /// <summary>
        /// Whether to enable the event bus
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Whether to enable event logging
        /// </summary>
        public bool EnableLogging { get; set; } = true;

        /// <summary>
        /// Whether to enable event validation
        /// </summary>
        public bool EnableValidation { get; set; } = true;

        /// <summary>
        /// Maximum number of concurrent event handlers
        /// </summary>
        public int MaxConcurrentHandlers { get; set; } = 10;

        /// <summary>
        /// Event handler timeout in seconds
        /// </summary>
        public int HandlerTimeoutSeconds { get; set; } = 30;

        /// <summary>
        /// Whether to enable event correlation
        /// </summary>
        public bool EnableCorrelation { get; set; } = true;

        /// <summary>
        /// Whether to enable event retry on failure
        /// </summary>
        public bool EnableRetry { get; set; } = true;

        /// <summary>
        /// Maximum number of retry attempts
        /// </summary>
        public int MaxRetryAttempts { get; set; } = 3;

        /// <summary>
        /// Retry delay in milliseconds
        /// </summary>
        public int RetryDelayMs { get; set; } = 1000;
    }

    /// <summary>
    /// Event bus statistics
    /// </summary>
    public class _EventBusStatistics
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
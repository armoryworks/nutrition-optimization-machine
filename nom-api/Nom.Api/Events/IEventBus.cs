// File: Nom.Api/_Abstractions/_Events/IEventBus.cs

using System;
using System.Threading.Tasks;
using Nom.Api.Events;

namespace Nom.Api.Events
{
    /// <summary>
    /// Interface for the event bus
    /// </summary>
    public interface IEventBus
    {
        /// <summary>
        /// Publishes an event to all registered handlers
        /// </summary>
        /// <typeparam name="TEvent">The type of event to publish</typeparam>
        /// <param name="event">The event to publish</param>
        /// <returns>Task representing the async operation</returns>
        Task PublishAsync<TEvent>(TEvent @event) where TEvent : IEvent;

        /// <summary>
        /// Subscribes to events of the specified type
        /// </summary>
        /// <typeparam name="TEvent">The type of event to subscribe to</typeparam>
        /// <param name="handler">The event handler</param>
        void Subscribe<TEvent>(IEventHandler<TEvent> handler) where TEvent : IEvent;

        /// <summary>
        /// Unsubscribes from events of the specified type
        /// </summary>
        /// <typeparam name="TEvent">The type of event to unsubscribe from</typeparam>
        /// <param name="handler">The event handler to unsubscribe</param>
        void Unsubscribe<TEvent>(IEventHandler<TEvent> handler) where TEvent : IEvent;

        /// <summary>
        /// Subscribes to events using a delegate
        /// </summary>
        /// <typeparam name="TEvent">The type of event to subscribe to</typeparam>
        /// <param name="handler">The delegate to handle the event</param>
        /// <param name="priority">The priority of the handler</param>
        void Subscribe<TEvent>(Func<TEvent, Task> handler, int priority = 0) where TEvent : IEvent;

        /// <summary>
        /// Unsubscribes from events using a delegate
        /// </summary>
        /// <typeparam name="TEvent">The type of event to unsubscribe from</typeparam>
        /// <param name="handler">The delegate to unsubscribe</param>
        void Unsubscribe<TEvent>(Func<TEvent, Task> handler) where TEvent : IEvent;

        /// <summary>
        /// Gets the number of subscribers for a specific event type
        /// </summary>
        /// <typeparam name="TEvent">The type of event</typeparam>
        /// <returns>The number of subscribers</returns>
        int GetSubscriberCount<TEvent>() where TEvent : IEvent;

        /// <summary>
        /// Clears all subscribers for a specific event type
        /// </summary>
        /// <typeparam name="TEvent">The type of event</typeparam>
        void ClearSubscribers<TEvent>() where TEvent : IEvent;

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
}
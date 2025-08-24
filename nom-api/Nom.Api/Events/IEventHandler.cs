using System.Threading.Tasks;
using Nom.Api.Events;

namespace Nom.Api.Events
{
    /// <summary>
    /// Interface for event handlers
    /// </summary>
    /// <typeparam name="TEvent">The type of event to handle</typeparam>
    public interface IEventHandler<in TEvent> where TEvent : IEvent
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
}


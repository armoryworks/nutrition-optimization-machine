using System.Threading.Tasks;
using Nom.Api.Events;

namespace Nom.Api.Events
{
    /// <summary>
    /// Base implementation for event handlers
    /// </summary>
    /// <typeparam name="TEvent">The type of event to handle</typeparam>
    public abstract class BaseEventHandler<TEvent> : IEventHandler<TEvent> where TEvent : IEvent
    {
        public virtual int Priority => 0;
        public virtual bool IsAsync => true;

        public abstract Task HandleAsync(TEvent @event);
    }
}






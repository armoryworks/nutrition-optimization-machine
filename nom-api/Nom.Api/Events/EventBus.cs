// File: Nom.Api/_Abstractions/_Events/EventBus.cs

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Nom.Api.Events
{
    /// <summary>
    /// Implementation of the event bus for pub-sub pattern
    /// </summary>
    public class EventBus : IEventBus
    {
        private readonly ILogger<EventBus> _logger;
        private readonly EventBusOptions _options;
        private readonly ConcurrentDictionary<Type, List<EventHandlerInfo>> _subscribers;
        private readonly ConcurrentDictionary<Type, List<DelegateHandlerInfo>> _delegateSubscribers;
        private readonly SemaphoreSlim _semaphore;
        private readonly EventBusStatistics _statistics;
        private bool _enabled;

        public EventBus(ILogger<EventBus> logger, IOptions<EventBusOptions> options)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _options = options?.Value ?? new EventBusOptions();
            _subscribers = new ConcurrentDictionary<Type, List<EventHandlerInfo>>();
            _delegateSubscribers = new ConcurrentDictionary<Type, List<DelegateHandlerInfo>>();
            _semaphore = new SemaphoreSlim(_options.MaxConcurrentHandlers, _options.MaxConcurrentHandlers);
            _statistics = new EventBusStatistics();
            _enabled = _options.Enabled;
        }

        public bool IsEnabled => _enabled;

        public void SetEnabled(bool enabled)
        {
            _enabled = enabled;
            _logger.LogInformation("Event bus {Status}", enabled ? "enabled" : "disabled");
        }

        public async Task PublishAsync<TEvent>(TEvent @event) where TEvent : IEvent
        {
            if (!_enabled)
            {
                _logger.LogDebug("Event bus is disabled, skipping event {EventType}", @event.EventType);
                return;
            }

            if (@event == null)
            {
                _logger.LogWarning("Attempted to publish null event");
                return;
            }

            if (_options.EnableValidation && !ValidateEvent(@event))
            {
                _logger.LogWarning("Event validation failed for {EventType}", @event.EventType);
                return;
            }

            _statistics.TotalEventsPublished++;
            _statistics.LastEventPublished = DateTime.UtcNow;

            if (_options.EnableLogging)
            {
                _logger.LogInformation("Publishing event {EventType} with ID {EventId}", @event.EventType, @event.EventId);
            }

            var eventType = typeof(TEvent);
            var handlers = new List<Task>();

            // Get interface-based handlers
            if (_subscribers.TryGetValue(eventType, out var interfaceHandlers))
            {
                foreach (var handlerInfo in interfaceHandlers.OrderBy(h => h.Priority))
                {
                    handlers.Add(ExecuteHandlerAsync(handlerInfo, @event));
                }
            }

            // Get delegate-based handlers
            if (_delegateSubscribers.TryGetValue(eventType, out var delegateHandlers))
            {
                foreach (var handlerInfo in delegateHandlers.OrderBy(h => h.Priority))
                {
                    handlers.Add(ExecuteDelegateHandlerAsync(handlerInfo, @event));
                }
            }

            if (handlers.Count > 0)
            {
                _statistics.ActiveSubscribers = handlers.Count;
                await Task.WhenAll(handlers);
            }
            else
            {
                _logger.LogDebug("No handlers registered for event {EventType}", @event.EventType);
            }
        }

        public void Subscribe<TEvent>(IEventHandler<TEvent> handler) where TEvent : IEvent
        {
            if (handler == null)
            {
                _logger.LogWarning("Attempted to subscribe null handler for {EventType}", typeof(TEvent).Name);
                return;
            }

            var eventType = typeof(TEvent);
            var handlerInfo = new EventHandlerInfo
            {
                Handler = handler,
                Priority = handler.Priority,
                IsAsync = handler.IsAsync
            };

            _subscribers.AddOrUpdate(
                eventType,
                new List<EventHandlerInfo> { handlerInfo },
                (key, existing) =>
                {
                    existing.Add(handlerInfo);
                    return existing;
                });

            _statistics.RegisteredEventTypes = _subscribers.Count;
            _logger.LogDebug("Subscribed handler {HandlerType} for event {EventType}", handler.GetType().Name, eventType.Name);
        }

        public void Unsubscribe<TEvent>(IEventHandler<TEvent> handler) where TEvent : IEvent
        {
            if (handler == null)
            {
                _logger.LogWarning("Attempted to unsubscribe null handler for {EventType}", typeof(TEvent).Name);
                return;
            }

            var eventType = typeof(TEvent);
            if (_subscribers.TryGetValue(eventType, out var handlers))
            {
                handlers.RemoveAll(h => h.Handler == handler);
                if (handlers.Count == 0)
                {
                    _subscribers.TryRemove(eventType, out _);
                }
            }

            _statistics.RegisteredEventTypes = _subscribers.Count;
            _logger.LogDebug("Unsubscribed handler {HandlerType} for event {EventType}", handler.GetType().Name, eventType.Name);
        }

        public void Subscribe<TEvent>(Func<TEvent, Task> handler, int priority = 0) where TEvent : IEvent
        {
            if (handler == null)
            {
                _logger.LogWarning("Attempted to subscribe null delegate handler for {EventType}", typeof(TEvent).Name);
                return;
            }

            var eventType = typeof(TEvent);
            var handlerInfo = new DelegateHandlerInfo
            {
                Handler = handler,
                Priority = priority
            };

            _delegateSubscribers.AddOrUpdate(
                eventType,
                new List<DelegateHandlerInfo> { handlerInfo },
                (key, existing) =>
                {
                    existing.Add(handlerInfo);
                    return existing;
                });

            _logger.LogDebug("Subscribed delegate handler for event {EventType}", eventType.Name);
        }

        public void Unsubscribe<TEvent>(Func<TEvent, Task> handler) where TEvent : IEvent
        {
            if (handler == null)
            {
                _logger.LogWarning("Attempted to unsubscribe null delegate handler for {EventType}", typeof(TEvent).Name);
                return;
            }

            var eventType = typeof(TEvent);
            if (_delegateSubscribers.TryGetValue(eventType, out var handlers))
            {
                handlers.RemoveAll(h => h.Handler == handler);
                if (handlers.Count == 0)
                {
                    _delegateSubscribers.TryRemove(eventType, out _);
                }
            }

            _logger.LogDebug("Unsubscribed delegate handler for event {EventType}", eventType.Name);
        }

        public int GetSubscriberCount<TEvent>() where TEvent : IEvent
        {
            var eventType = typeof(TEvent);
            var count = 0;

            if (_subscribers.TryGetValue(eventType, out var interfaceHandlers))
            {
                count += interfaceHandlers.Count;
            }

            if (_delegateSubscribers.TryGetValue(eventType, out var delegateHandlers))
            {
                count += delegateHandlers.Count;
            }

            return count;
        }

        public void ClearSubscribers<TEvent>() where TEvent : IEvent
        {
            var eventType = typeof(TEvent);
            _subscribers.TryRemove(eventType, out _);
            _delegateSubscribers.TryRemove(eventType, out _);
            _logger.LogInformation("Cleared all subscribers for event {EventType}", eventType.Name);
        }

        public void ClearAllSubscribers()
        {
            _subscribers.Clear();
            _delegateSubscribers.Clear();
            _statistics.RegisteredEventTypes = 0;
            _logger.LogInformation("Cleared all subscribers for all event types");
        }

        public EventBusStatistics GetStatistics()
        {
            return new EventBusStatistics
            {
                TotalEventsPublished = _statistics.TotalEventsPublished,
                TotalEventsHandled = _statistics.TotalEventsHandled,
                TotalHandlerErrors = _statistics.TotalHandlerErrors,
                AverageProcessingTimeMs = _statistics.AverageProcessingTimeMs,
                ActiveSubscribers = _statistics.ActiveSubscribers,
                RegisteredEventTypes = _statistics.RegisteredEventTypes,
                LastEventPublished = _statistics.LastEventPublished,
                LastEventHandled = _statistics.LastEventHandled
            };
        }

        private async Task ExecuteHandlerAsync<TEvent>(EventHandlerInfo handlerInfo, TEvent @event) where TEvent : IEvent
        {
            var startTime = DateTime.UtcNow;
            var retryCount = 0;

            while (retryCount <= _options.MaxRetryAttempts)
            {
                try
                {
                    await _semaphore.WaitAsync();
                    try
                    {
                        if (_options.HandlerTimeoutSeconds > 0)
                        {
                            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(_options.HandlerTimeoutSeconds));
                            await ((IEventHandler<TEvent>)handlerInfo.Handler).HandleAsync(@event).WaitAsync(cts.Token);
                        }
                        else
                        {
                            await ((IEventHandler<TEvent>)handlerInfo.Handler).HandleAsync(@event);
                        }

                        _statistics.TotalEventsHandled++;
                        _statistics.LastEventHandled = DateTime.UtcNow;

                        var processingTime = (DateTime.UtcNow - startTime).TotalMilliseconds;
                        _statistics.AverageProcessingTimeMs = (_statistics.AverageProcessingTimeMs + processingTime) / 2;

                        if (_options.EnableLogging)
                        {
                            _logger.LogDebug("Successfully handled event {EventType} with handler {HandlerType}",
                                @event.EventType, handlerInfo.Handler.GetType().Name);
                        }

                        return;
                    }
                    finally
                    {
                        _semaphore.Release();
                    }
                }
                catch (Exception ex)
                {
                    _statistics.TotalHandlerErrors++;
                    _logger.LogError(ex, "Error handling event {EventType} with handler {HandlerType} (attempt {Attempt}/{MaxAttempts})",
                        @event.EventType, handlerInfo.Handler.GetType().Name, retryCount + 1, _options.MaxRetryAttempts + 1);

                    if (retryCount < _options.MaxRetryAttempts && _options.EnableRetry)
                    {
                        retryCount++;
                        await Task.Delay(_options.RetryDelayMs);
                        continue;
                    }

                    throw;
                }
            }
        }

        private async Task ExecuteDelegateHandlerAsync<TEvent>(DelegateHandlerInfo handlerInfo, TEvent @event) where TEvent : IEvent
        {
            var startTime = DateTime.UtcNow;
            var retryCount = 0;

            while (retryCount <= _options.MaxRetryAttempts)
            {
                try
                {
                    await _semaphore.WaitAsync();
                    try
                    {
                        if (_options.HandlerTimeoutSeconds > 0)
                        {
                            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(_options.HandlerTimeoutSeconds));
                            await ((Func<TEvent, Task>)handlerInfo.Handler)(@event).WaitAsync(cts.Token);
                        }
                        else
                        {
                            await ((Func<TEvent, Task>)handlerInfo.Handler)(@event);
                        }

                        _statistics.TotalEventsHandled++;
                        _statistics.LastEventHandled = DateTime.UtcNow;

                        var processingTime = (DateTime.UtcNow - startTime).TotalMilliseconds;
                        _statistics.AverageProcessingTimeMs = (_statistics.AverageProcessingTimeMs + processingTime) / 2;

                        if (_options.EnableLogging)
                        {
                            _logger.LogDebug("Successfully handled event {EventType} with delegate handler", @event.EventType);
                        }

                        return;
                    }
                    finally
                    {
                        _semaphore.Release();
                    }
                }
                catch (Exception ex)
                {
                    _statistics.TotalHandlerErrors++;
                    _logger.LogError(ex, "Error handling event {EventType} with delegate handler (attempt {Attempt}/{MaxAttempts})",
                        @event.EventType, retryCount + 1, _options.MaxRetryAttempts + 1);

                    if (retryCount < _options.MaxRetryAttempts && _options.EnableRetry)
                    {
                        retryCount++;
                        await Task.Delay(_options.RetryDelayMs);
                        continue;
                    }

                    throw;
                }
            }
        }

        private bool ValidateEvent(IEvent @event)
        {
            if (string.IsNullOrWhiteSpace(@event.EventType))
            {
                _logger.LogWarning("Event validation failed: EventType is null or empty");
                return false;
            }

            if (string.IsNullOrWhiteSpace(@event.Source))
            {
                _logger.LogWarning("Event validation failed: Source is null or empty");
                return false;
            }

            return true;
        }

        public void Dispose()
        {
            _semaphore?.Dispose();
        }
    }
}
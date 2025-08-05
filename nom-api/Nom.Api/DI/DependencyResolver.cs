// File: Nom.Api/_Abstractions/_DI/DependencyResolver.cs

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Nom.Api.DI
{
    /// <summary>
    /// Dependency resolver implementation for managing service resolution and lifecycle
    /// </summary>
    public class DependencyResolver : IDependencyResolver
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<DependencyResolver> _logger;
        private readonly ConcurrentDictionary<Type, int> _resolutionCounts;
        private readonly ConcurrentQueue<long> _resolutionTimes;
        private readonly ConcurrentDictionary<Type, ServiceLifetime> _serviceLifetimes;
        private readonly object _statisticsLock = new object();
        private bool _disposed = false;

        public IServiceProvider ServiceProvider => _serviceProvider;

        public DependencyResolver(IServiceProvider serviceProvider, ILogger<DependencyResolver> logger)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _resolutionCounts = new ConcurrentDictionary<Type, int>();
            _resolutionTimes = new ConcurrentQueue<long>();
            _serviceLifetimes = new ConcurrentDictionary<Type, ServiceLifetime>();

            _logger.LogInformation("Dependency resolver initialized");
        }

        public T Resolve<T>() where T : class
        {
            return (T)Resolve(typeof(T));
        }

        public object Resolve(Type serviceType)
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                _logger.LogDebug("Resolving service: {ServiceType}", serviceType.Name);

                var service = _serviceProvider.GetService(serviceType);
                if (service == null)
                {
                    _logger.LogWarning("Failed to resolve service: {ServiceType}", serviceType.Name);
                    TrackFailedResolution(serviceType, stopwatch.ElapsedMilliseconds);
                    throw new InvalidOperationException($"Service of type {serviceType.Name} is not registered.");
                }

                TrackSuccessfulResolution(serviceType, stopwatch.ElapsedMilliseconds);
                return service;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resolving service: {ServiceType}", serviceType.Name);
                TrackFailedResolution(serviceType, stopwatch.ElapsedMilliseconds);
                throw;
            }
        }

        public IEnumerable<T> ResolveAll<T>() where T : class
        {
            return ResolveAll(typeof(T)).Cast<T>();
        }

        public IEnumerable<object?> ResolveAll(Type serviceType)
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                _logger.LogDebug("Resolving all services: {ServiceType}", serviceType.Name);

                var services = _serviceProvider.GetServices(serviceType).ToList();
                TrackSuccessfulResolution(serviceType, stopwatch.ElapsedMilliseconds);
                return services;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resolving all services: {ServiceType}", serviceType.Name);
                TrackFailedResolution(serviceType, stopwatch.ElapsedMilliseconds);
                throw;
            }
        }

        public bool TryResolve<T>(out T? service) where T : class
        {
            var result = TryResolve(typeof(T), out var obj);
            service = obj as T;
            return result;
        }

        public bool TryResolve(Type serviceType, out object? service)
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                _logger.LogDebug("Trying to resolve service: {ServiceType}", serviceType.Name);

                service = _serviceProvider.GetService(serviceType);
                var success = service != null;

                if (success)
                {
                    TrackSuccessfulResolution(serviceType, stopwatch.ElapsedMilliseconds);
                }
                else
                {
                    TrackFailedResolution(serviceType, stopwatch.ElapsedMilliseconds);
                }

                return success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error trying to resolve service: {ServiceType}", serviceType.Name);
                TrackFailedResolution(serviceType, stopwatch.ElapsedMilliseconds);
                service = null;
                return false;
            }
        }

        public IDependencyResolver CreateScope()
        {
            try
            {
                var scope = _serviceProvider.CreateScope();
                var resolver = new DependencyResolver(scope.ServiceProvider, _logger);
                _logger.LogDebug("Created new dependency resolver scope");
                return resolver;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating dependency resolver scope");
                throw;
            }
        }

        public IDependencyResolver GetCurrentScope()
        {
            return this;
        }

        public DependencyResolutionValidationResult ValidateResolution()
        {
            var result = new DependencyResolutionValidationResult
            {
                IsValid = true
            };

            try
            {
                // Get all registered service descriptors
                var serviceCollection = _serviceProvider.GetType()
                    .GetProperty("Services", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?
                    .GetValue(_serviceProvider) as IServiceCollection;

                if (serviceCollection != null)
                {
                    foreach (var descriptor in serviceCollection)
                    {
                        try
                        {
                            // Try to resolve the service
                            var service = _serviceProvider.GetService(descriptor.ServiceType);
                            if (service == null)
                            {
                                result.UnresolvableServices.Add(descriptor.ServiceType);
                                result.Errors.Add($"Service {descriptor.ServiceType.Name} cannot be resolved");
                            }
                        }
                        catch (Exception ex)
                        {
                            result.UnresolvableServices.Add(descriptor.ServiceType);
                            result.Errors.Add($"Service {descriptor.ServiceType.Name} cannot be resolved: {ex.Message}");
                        }
                    }
                }

                result.IsValid = result.Errors.Count == 0;
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Error during validation: {ex.Message}");
                result.IsValid = false;
            }

            return result;
        }

        public DependencyResolutionStatistics GetStatistics()
        {
            lock (_statisticsLock)
            {
                var recentResolutionTimes = _resolutionTimes.Take(100).ToArray();
                var averageResolutionTime = recentResolutionTimes.Length > 0 ? recentResolutionTimes.Average() : 0;

                return new DependencyResolutionStatistics
                {
                    TotalResolutions = _resolutionCounts.Values.Sum(),
                    SuccessfulResolutions = _resolutionCounts.Values.Sum(), // Simplified for now
                    FailedResolutions = 0, // Would need to track this separately
                    SingletonResolutions = _serviceLifetimes.Count(kvp => kvp.Value == ServiceLifetime.Singleton),
                    ScopedResolutions = _serviceLifetimes.Count(kvp => kvp.Value == ServiceLifetime.Scoped),
                    TransientResolutions = _serviceLifetimes.Count(kvp => kvp.Value == ServiceLifetime.Transient),
                    AverageResolutionTimeMs = averageResolutionTime,
                    TotalResolutionTimeMs = recentResolutionTimes.Sum(),
                    ActiveScopes = 1, // Simplified for now
                    DisposedScopes = 0, // Would need to track this separately
                    ServiceTypeCounts = new Dictionary<string, int>(_resolutionCounts.ToDictionary(kvp => kvp.Key.Name, kvp => kvp.Value)),
                    LastUpdated = DateTime.UtcNow
                };
            }
        }

        public List<Type> GetRegisteredServices()
        {
            try
            {
                var serviceCollection = _serviceProvider.GetType()
                    .GetProperty("Services", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?
                    .GetValue(_serviceProvider) as IServiceCollection;

                return serviceCollection?.Select(d => d.ServiceType).ToList() ?? new List<Type>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting registered services");
                return new List<Type>();
            }
        }

        public bool IsRegistered<T>() where T : class
        {
            return IsRegistered(typeof(T));
        }

        public bool IsRegistered(Type serviceType)
        {
            try
            {
                var service = _serviceProvider.GetService(serviceType);
                return service != null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking if service is registered: {ServiceType}", serviceType.Name);
                return false;
            }
        }

        private void TrackSuccessfulResolution(Type serviceType, long resolutionTimeMs)
        {
            _resolutionCounts.AddOrUpdate(serviceType, 1, (k, v) => v + 1);
            _resolutionTimes.Enqueue(resolutionTimeMs);

            // Keep only the last 1000 resolution times to prevent memory leaks
            while (_resolutionTimes.Count > 1000)
            {
                _resolutionTimes.TryDequeue(out _);
            }

            _logger.LogDebug("Successfully resolved {ServiceType} in {ResolutionTime}ms", serviceType.Name, resolutionTimeMs);
        }

        private void TrackFailedResolution(Type serviceType, long resolutionTimeMs)
        {
            _logger.LogWarning("Failed to resolve {ServiceType} in {ResolutionTime}ms", serviceType.Name, resolutionTimeMs);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _logger.LogInformation("Dependency resolver disposed");
            }
        }
    }
}
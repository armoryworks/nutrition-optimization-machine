// File: Nom.Api/_Abstractions/_Factories/OrchestrationServiceFactory.cs

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nom.Api.Core;
using Nom.Api.Factories.Interfaces;

namespace Nom.Api.Factories
{
    /// <summary>
    /// Factory implementation for creating orchestration services with dependency injection
    /// </summary>
    public class OrchestrationServiceFactory : IOrchestrationServiceFactory
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<OrchestrationServiceFactory> _logger;
        private readonly ConcurrentDictionary<Type, ServiceRegistrationInfo> _serviceRegistrations;
        private readonly ConcurrentDictionary<Type, object> _serviceCache;
        private readonly OrchestrationServiceFactoryOptions _options;

        public OrchestrationServiceFactory(
            IServiceProvider serviceProvider,
            ILogger<OrchestrationServiceFactory> logger,
            OrchestrationServiceFactoryOptions? options = null)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _options = options ?? new OrchestrationServiceFactoryOptions();
            _serviceRegistrations = new ConcurrentDictionary<Type, ServiceRegistrationInfo>();
            _serviceCache = new ConcurrentDictionary<Type, object>();
        }

        public IServiceProvider ServiceProvider => _serviceProvider;

        public TService CreateService<TService>() where TService : class
        {
            try
            {
                var serviceType = typeof(TService);
                _logger.LogDebug("Creating service of type {ServiceType}", serviceType.Name);

                if (_options.EnableCaching && _serviceCache.TryGetValue(serviceType, out var cachedService))
                {
                    _logger.LogDebug("Returning cached service {ServiceType}", serviceType.Name);
                    return (TService)cachedService;
                }

                var service = _serviceProvider.GetService<TService>();
                if (service == null)
                {
                    throw new InvalidOperationException($"Service of type {serviceType.Name} is not registered in the service provider");
                }

                if (_options.EnableValidation)
                {
                    ValidateService(service);
                }

                if (_options.EnableCaching)
                {
                    _serviceCache.TryAdd(serviceType, service);
                }

                if (_options.EnableLogging)
                {
                    _logger.LogInformation("Successfully created service {ServiceType}", serviceType.Name);
                }

                return service;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating service of type {ServiceType}", typeof(TService).Name);
                throw;
            }
        }

        public TService CreateService<TService>(params object[] parameters) where TService : class
        {
            try
            {
                var serviceType = typeof(TService);
                _logger.LogDebug("Creating service of type {ServiceType} with custom parameters", serviceType.Name);

                // Create service using ActivatorUtilities for custom parameters
                var service = ActivatorUtilities.CreateInstance<TService>(_serviceProvider, parameters);

                if (_options.EnableValidation)
                {
                    ValidateService(service);
                }

                if (_options.EnableLogging)
                {
                    _logger.LogInformation("Successfully created service {ServiceType} with custom parameters", serviceType.Name);
                }

                return service;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating service of type {ServiceType} with custom parameters", typeof(TService).Name);
                throw;
            }
        }

        public IBaseOrchestrationService<TEntity, TCreateModel, TUpdateModel, TResponseModel, TId>
            CreateBaseService<TEntity, TCreateModel, TUpdateModel, TResponseModel, TId>()
            where TEntity : class
            where TCreateModel : class
            where TUpdateModel : class
            where TResponseModel : class
            where TId : struct
        {
            try
            {
                _logger.LogDebug("Creating base orchestration service for entity type {EntityType}", typeof(TEntity).Name);

                // Create a generic base service using reflection
                var baseServiceType = typeof(BaseOrchestrationService<,,,,>).MakeGenericType(
                    typeof(TEntity), typeof(TCreateModel), typeof(TUpdateModel), typeof(TResponseModel), typeof(TId));

                var service = ActivatorUtilities.CreateInstance(_serviceProvider, baseServiceType);

                if (_options.EnableValidation)
                {
                    ValidateService(service);
                }

                if (_options.EnableLogging)
                {
                    _logger.LogInformation("Successfully created base orchestration service for entity type {EntityType}", typeof(TEntity).Name);
                }

                return (IBaseOrchestrationService<TEntity, TCreateModel, TUpdateModel, TResponseModel, TId>)service;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating base orchestration service for entity type {EntityType}", typeof(TEntity).Name);
                throw;
            }
        }

        public IReadOnlyOrchestrationService<TEntity, TResponseModel, TId>
            CreateReadOnlyService<TEntity, TResponseModel, TId>()
            where TEntity : class
            where TResponseModel : class
            where TId : struct
        {
            try
            {
                _logger.LogDebug("Creating read-only orchestration service for entity type {EntityType}", typeof(TEntity).Name);

                // Create a generic read-only service using reflection
                var readOnlyServiceType = typeof(ReadOnlyOrchestrationService<,,>).MakeGenericType(
                    typeof(TEntity), typeof(TResponseModel), typeof(TId));

                var service = ActivatorUtilities.CreateInstance(_serviceProvider, readOnlyServiceType);

                if (_options.EnableValidation)
                {
                    ValidateService(service);
                }

                if (_options.EnableLogging)
                {
                    _logger.LogInformation("Successfully created read-only orchestration service for entity type {EntityType}", typeof(TEntity).Name);
                }

                return (IReadOnlyOrchestrationService<TEntity, TResponseModel, TId>)service;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating read-only orchestration service for entity type {EntityType}", typeof(TEntity).Name);
                throw;
            }
        }

        public void RegisterService<TService, TImplementation>()
            where TService : class
            where TImplementation : class, TService
        {
            try
            {
                var serviceType = typeof(TService);
                var implementationType = typeof(TImplementation);

                _logger.LogDebug("Registering service {ServiceType} with implementation {ImplementationType}",
                    serviceType.Name, implementationType.Name);

                var registrationInfo = new ServiceRegistrationInfo
                {
                    ServiceType = serviceType,
                    ImplementationType = implementationType,
                    Lifetime = ServiceLifetime.Scoped,
                    IsRegistered = true,
                    RegistrationTime = DateTime.UtcNow
                };

                _serviceRegistrations.AddOrUpdate(serviceType, registrationInfo, (key, existing) => registrationInfo);

                if (_options.EnableLogging)
                {
                    _logger.LogInformation("Successfully registered service {ServiceType} with implementation {ImplementationType}",
                        serviceType.Name, implementationType.Name);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error registering service {ServiceType} with implementation {ImplementationType}",
                    typeof(TService).Name, typeof(TImplementation).Name);
                throw;
            }
        }

        public void RegisterService<TService>(Func<IServiceProvider, TService> factoryMethod)
            where TService : class
        {
            try
            {
                var serviceType = typeof(TService);

                _logger.LogDebug("Registering service {ServiceType} with factory method", serviceType.Name);

                var registrationInfo = new ServiceRegistrationInfo
                {
                    ServiceType = serviceType,
                    ImplementationType = serviceType,
                    Lifetime = ServiceLifetime.Scoped,
                    FactoryMethod = factoryMethod,
                    IsRegistered = true,
                    RegistrationTime = DateTime.UtcNow
                };

                _serviceRegistrations.AddOrUpdate(serviceType, registrationInfo, (key, existing) => registrationInfo);

                if (_options.EnableLogging)
                {
                    _logger.LogInformation("Successfully registered service {ServiceType} with factory method", serviceType.Name);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error registering service {ServiceType} with factory method", typeof(TService).Name);
                throw;
            }
        }

        public bool IsServiceRegistered<TService>() where TService : class
        {
            var serviceType = typeof(TService);
            return _serviceRegistrations.ContainsKey(serviceType) && _serviceRegistrations[serviceType].IsRegistered;
        }

        /// <summary>
        /// Validates a service instance
        /// </summary>
        /// <param name="service">The service to validate</param>
        private void ValidateService(object service)
        {
            if (service == null)
            {
                throw new ArgumentNullException(nameof(service), "Service cannot be null");
            }

            // Add additional validation logic here if needed
            _logger.LogDebug("Service validation passed for {ServiceType}", service.GetType().Name);
        }

        /// <summary>
        /// Clears the service cache
        /// </summary>
        public void ClearCache()
        {
            _serviceCache.Clear();
            _logger.LogInformation("Service cache cleared");
        }

        /// <summary>
        /// Gets all registered service types
        /// </summary>
        /// <returns>List of registered service types</returns>
        public IEnumerable<Type> GetRegisteredServiceTypes()
        {
            return _serviceRegistrations.Keys;
        }

        /// <summary>
        /// Gets registration information for a service type
        /// </summary>
        /// <param name="serviceType">The service type</param>
        /// <returns>Registration information</returns>
        public ServiceRegistrationInfo? GetRegistrationInfo(Type serviceType)
        {
            return _serviceRegistrations.TryGetValue(serviceType, out var info) ? info : null;
        }

        /// <summary>
        /// Disposes the factory and cleans up resources
        /// </summary>
        public void Dispose()
        {
            _serviceCache.Clear();
            _serviceRegistrations.Clear();
            _logger.LogInformation("Orchestration service factory disposed");
        }
    }
}
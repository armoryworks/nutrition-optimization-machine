// File: Nom.Api/_Abstractions/_Factories/_IOrchestrationServiceFactory.cs

using System;
using Microsoft.Extensions.DependencyInjection;
using Nom.Api._Abstractions._Core;

namespace Nom.Api._Abstractions._Factories
{
    /// <summary>
    /// Factory interface for creating orchestration services with dependency injection
    /// </summary>
    public interface _IOrchestrationServiceFactory
    {
        /// <summary>
        /// Creates an orchestration service of the specified type
        /// </summary>
        /// <typeparam name="TService">The type of service to create</typeparam>
        /// <returns>The created service instance</returns>
        TService CreateService<TService>() where TService : class;

        /// <summary>
        /// Creates an orchestration service of the specified type with custom parameters
        /// </summary>
        /// <typeparam name="TService">The type of service to create</typeparam>
        /// <param name="parameters">Custom parameters for service creation</param>
        /// <returns>The created service instance</returns>
        TService CreateService<TService>(params object[] parameters) where TService : class;

        /// <summary>
        /// Creates a base orchestration service with generic type parameters
        /// </summary>
        /// <typeparam name="TEntity">The entity type</typeparam>
        /// <typeparam name="TCreateModel">The create model type</typeparam>
        /// <typeparam name="TUpdateModel">The update model type</typeparam>
        /// <typeparam name="TResponseModel">The response model type</typeparam>
        /// <typeparam name="TId">The ID type</typeparam>
        /// <returns>The created base orchestration service</returns>
        _IBaseOrchestrationService<TEntity, TCreateModel, TUpdateModel, TResponseModel, TId> 
            CreateBaseService<TEntity, TCreateModel, TUpdateModel, TResponseModel, TId>()
            where TEntity : class
            where TCreateModel : class
            where TUpdateModel : class
            where TResponseModel : class
            where TId : struct;

        /// <summary>
        /// Creates a read-only orchestration service
        /// </summary>
        /// <typeparam name="TEntity">The entity type</typeparam>
        /// <typeparam name="TResponseModel">The response model type</typeparam>
        /// <typeparam name="TId">The ID type</typeparam>
        /// <returns>The created read-only orchestration service</returns>
        _IReadOnlyOrchestrationService<TEntity, TResponseModel, TId> 
            CreateReadOnlyService<TEntity, TResponseModel, TId>()
            where TEntity : class
            where TResponseModel : class
            where TId : struct;

        /// <summary>
        /// Registers a service type with the factory
        /// </summary>
        /// <typeparam name="TService">The service type to register</typeparam>
        /// <typeparam name="TImplementation">The implementation type</typeparam>
        void RegisterService<TService, TImplementation>() 
            where TService : class 
            where TImplementation : class, TService;

        /// <summary>
        /// Registers a service type with the factory using a factory method
        /// </summary>
        /// <typeparam name="TService">The service type to register</typeparam>
        /// <param name="factoryMethod">The factory method to create the service</param>
        void RegisterService<TService>(Func<IServiceProvider, TService> factoryMethod) 
            where TService : class;

        /// <summary>
        /// Checks if a service type is registered with the factory
        /// </summary>
        /// <typeparam name="TService">The service type to check</typeparam>
        /// <returns>True if registered, false otherwise</returns>
        bool IsServiceRegistered<TService>() where TService : class;

        /// <summary>
        /// Gets the service provider used by this factory
        /// </summary>
        IServiceProvider ServiceProvider { get; }
    }

    /// <summary>
    /// Configuration options for the orchestration service factory
    /// </summary>
    public class _OrchestrationServiceFactoryOptions
    {
        /// <summary>
        /// Whether to enable automatic service discovery
        /// </summary>
        public bool EnableAutoDiscovery { get; set; } = true;

        /// <summary>
        /// Whether to enable service caching
        /// </summary>
        public bool EnableCaching { get; set; } = true;

        /// <summary>
        /// Cache duration for services (in minutes)
        /// </summary>
        public int CacheDurationMinutes { get; set; } = 30;

        /// <summary>
        /// Whether to enable service validation
        /// </summary>
        public bool EnableValidation { get; set; } = true;

        /// <summary>
        /// Whether to enable service logging
        /// </summary>
        public bool EnableLogging { get; set; } = true;
    }

    /// <summary>
    /// Service registration information
    /// </summary>
    public class _ServiceRegistrationInfo
    {
        /// <summary>
        /// The service type
        /// </summary>
        public Type ServiceType { get; set; } = null!;

        /// <summary>
        /// The implementation type
        /// </summary>
        public Type ImplementationType { get; set; } = null!;

        /// <summary>
        /// The lifetime of the service
        /// </summary>
        public ServiceLifetime Lifetime { get; set; }

        /// <summary>
        /// The factory method (if using factory registration)
        /// </summary>
        public Delegate? FactoryMethod { get; set; }

        /// <summary>
        /// Whether the service is registered
        /// </summary>
        public bool IsRegistered { get; set; }

        /// <summary>
        /// Registration timestamp
        /// </summary>
        public DateTime RegistrationTime { get; set; }
    }
} 

using System;
using Microsoft.Extensions.DependencyInjection;
using Nom.Api.Core;

namespace Nom.Api.Factories.Interfaces
{
    /// <summary>
    /// Factory interface for creating orchestration services with dependency injection
    /// </summary>
    public interface IOrchestrationServiceFactory
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
        IBaseOrchestrationService<TEntity, TCreateModel, TUpdateModel, TResponseModel, TId>
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
        IReadOnlyOrchestrationService<TEntity, TResponseModel, TId>
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
}
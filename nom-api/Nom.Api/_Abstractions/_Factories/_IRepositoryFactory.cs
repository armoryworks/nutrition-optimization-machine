// File: Nom.Api/_Abstractions/_Factories/_IRepositoryFactory.cs

using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nom.Api._Abstractions._Core;

namespace Nom.Api._Abstractions._Factories
{
    /// <summary>
    /// Factory interface for creating repository instances with proper dependency injection
    /// and lifecycle management.
    /// </summary>
    public interface _IRepositoryFactory
    {
        /// <summary>
        /// Gets the service provider for dependency injection
        /// </summary>
        IServiceProvider ServiceProvider { get; }

        /// <summary>
        /// Gets the logger for factory operations
        /// </summary>
        ILogger Logger { get; }

        /// <summary>
        /// Creates a repository instance for the specified entity type
        /// </summary>
        /// <typeparam name="TEntity">The entity type</typeparam>
        /// <typeparam name="TId">The primary key type</typeparam>
        /// <returns>A repository instance</returns>
        _IBaseRepository<TEntity, TId> CreateRepository<TEntity, TId>()
            where TEntity : class
            where TId : struct;

        /// <summary>
        /// Creates a read-only repository instance for the specified entity type
        /// </summary>
        /// <typeparam name="TEntity">The entity type</typeparam>
        /// <typeparam name="TId">The primary key type</typeparam>
        /// <returns>A read-only repository instance</returns>
        _IReadOnlyRepository<TEntity, TId> CreateReadOnlyRepository<TEntity, TId>()
            where TEntity : class
            where TId : struct;

        /// <summary>
        /// Creates a repository instance with custom configuration
        /// </summary>
        /// <typeparam name="TEntity">The entity type</typeparam>
        /// <typeparam name="TId">The primary key type</typeparam>
        /// <param name="options">Repository configuration options</param>
        /// <returns>A repository instance</returns>
        _IBaseRepository<TEntity, TId> CreateRepository<TEntity, TId>(_RepositoryFactoryOptions options)
            where TEntity : class
            where TId : struct;

        /// <summary>
        /// Registers a repository type with the factory
        /// </summary>
        /// <typeparam name="TEntity">The entity type</typeparam>
        /// <typeparam name="TId">The primary key type</typeparam>
        /// <typeparam name="TRepository">The repository implementation type</typeparam>
        void RegisterRepository<TEntity, TId, TRepository>()
            where TEntity : class
            where TId : struct
            where TRepository : class, _IBaseRepository<TEntity, TId>;

        /// <summary>
        /// Registers a read-only repository type with the factory
        /// </summary>
        /// <typeparam name="TEntity">The entity type</typeparam>
        /// <typeparam name="TId">The primary key type</typeparam>
        /// <typeparam name="TRepository">The repository implementation type</typeparam>
        void RegisterReadOnlyRepository<TEntity, TId, TRepository>()
            where TEntity : class
            where TId : struct
            where TRepository : class, _IReadOnlyRepository<TEntity, TId>;

        /// <summary>
        /// Gets all registered repository types
        /// </summary>
        /// <returns>Dictionary of registered repository types</returns>
        Dictionary<string, Type> GetRegisteredRepositories();

        /// <summary>
        /// Validates that a repository can be created for the specified entity type
        /// </summary>
        /// <typeparam name="TEntity">The entity type</typeparam>
        /// <typeparam name="TId">The primary key type</typeparam>
        /// <returns>True if repository can be created, false otherwise</returns>
        bool CanCreateRepository<TEntity, TId>()
            where TEntity : class
            where TId : struct;

        /// <summary>
        /// Disposes all created repository instances
        /// </summary>
        void DisposeAllRepositories();

        /// <summary>
        /// Gets statistics about repository creation and usage
        /// </summary>
        /// <returns>Repository factory statistics</returns>
        _RepositoryFactoryStatistics GetStatistics();
    }
} 
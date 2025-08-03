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

    /// <summary>
    /// Configuration options for repository factory
    /// </summary>
    public class _RepositoryFactoryOptions
    {
        /// <summary>
        /// Whether to enable caching for the repository
        /// </summary>
        public bool EnableCaching { get; set; } = true;

        /// <summary>
        /// Cache duration in seconds
        /// </summary>
        public int CacheDurationSeconds { get; set; } = 300;

        /// <summary>
        /// Whether to enable logging for the repository
        /// </summary>
        public bool EnableLogging { get; set; } = true;

        /// <summary>
        /// Whether to enable performance monitoring
        /// </summary>
        public bool EnablePerformanceMonitoring { get; set; } = true;

        /// <summary>
        /// Maximum number of concurrent operations
        /// </summary>
        public int MaxConcurrentOperations { get; set; } = 10;

        /// <summary>
        /// Whether to enable retry logic
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
    /// Statistics for repository factory operations
    /// </summary>
    public class _RepositoryFactoryStatistics
    {
        /// <summary>
        /// Total number of repositories created
        /// </summary>
        public int TotalRepositoriesCreated { get; set; }

        /// <summary>
        /// Number of currently active repositories
        /// </summary>
        public int ActiveRepositories { get; set; }

        /// <summary>
        /// Number of disposed repositories
        /// </summary>
        public int DisposedRepositories { get; set; }

        /// <summary>
        /// Average creation time in milliseconds
        /// </summary>
        public double AverageCreationTimeMs { get; set; }

        /// <summary>
        /// Total creation time in milliseconds
        /// </summary>
        public double TotalCreationTimeMs { get; set; }

        /// <summary>
        /// Number of failed repository creations
        /// </summary>
        public int FailedCreations { get; set; }

        /// <summary>
        /// Repository types and their creation counts
        /// </summary>
        public Dictionary<string, int> RepositoryTypeCounts { get; set; } = new();

        /// <summary>
        /// Timestamp of the last statistics update
        /// </summary>
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    }
} 
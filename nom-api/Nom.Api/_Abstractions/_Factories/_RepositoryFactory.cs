// File: Nom.Api/_Abstractions/_Factories/_RepositoryFactory.cs

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nom.Api._Abstractions._Core;
using System.Threading.Tasks;

namespace Nom.Api._Abstractions._Factories
{
    /// <summary>
    /// Factory implementation for creating repository instances with proper dependency injection,
    /// lifecycle management, and performance monitoring.
    /// </summary>
    public class _RepositoryFactory : _IRepositoryFactory, IDisposable
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<_RepositoryFactory> _logger;
        private readonly ConcurrentDictionary<string, Type> _registeredRepositories;
        private readonly ConcurrentDictionary<string, object> _activeRepositories;
        private readonly ConcurrentDictionary<string, int> _repositoryTypeCounts;
        private readonly ConcurrentQueue<long> _creationTimes;
        private readonly object _statisticsLock = new object();
        private bool _disposed = false;

        public IServiceProvider ServiceProvider => _serviceProvider;
        public ILogger Logger => _logger;

        public _RepositoryFactory(IServiceProvider serviceProvider, ILogger<_RepositoryFactory> logger)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _registeredRepositories = new ConcurrentDictionary<string, Type>();
            _activeRepositories = new ConcurrentDictionary<string, object>();
            _repositoryTypeCounts = new ConcurrentDictionary<string, int>();
            _creationTimes = new ConcurrentQueue<long>();

            _logger.LogInformation("Repository factory initialized");
        }

        public _IBaseRepository<TEntity, TId> CreateRepository<TEntity, TId>()
            where TEntity : class
            where TId : struct
        {
            return CreateRepository<TEntity, TId>(new _RepositoryFactoryOptions());
        }

        public _IReadOnlyRepository<TEntity, TId> CreateReadOnlyRepository<TEntity, TId>()
            where TEntity : class
            where TId : struct
        {
            var stopwatch = Stopwatch.StartNew();
            var entityType = typeof(TEntity);
            var idType = typeof(TId);
            var key = $"ReadOnly_{entityType.Name}_{idType.Name}";

            try
            {
                _logger.LogDebug("Creating read-only repository for {EntityType} with {IdType}", entityType.Name, idType.Name);

                // Check if we have a registered read-only repository
                if (_registeredRepositories.TryGetValue(key, out var registeredType))
                {
                    var repository = (_IReadOnlyRepository<TEntity, TId>)ActivatorUtilities.CreateInstance(_serviceProvider, registeredType);
                    TrackRepositoryCreation(key, stopwatch.ElapsedMilliseconds);
                    return repository;
                }

                // Create a default read-only repository
                var defaultRepository = new _ReadOnlyRepository<TEntity, TId>(_serviceProvider, _logger);
                TrackRepositoryCreation(key, stopwatch.ElapsedMilliseconds);
                return defaultRepository;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create read-only repository for {EntityType}", entityType.Name);
                TrackFailedCreation(key);
                throw;
            }
        }

        public _IBaseRepository<TEntity, TId> CreateRepository<TEntity, TId>(_RepositoryFactoryOptions options)
            where TEntity : class
            where TId : struct
        {
            var stopwatch = Stopwatch.StartNew();
            var entityType = typeof(TEntity);
            var idType = typeof(TId);
            var key = $"{entityType.Name}_{idType.Name}";

            try
            {
                _logger.LogDebug("Creating repository for {EntityType} with {IdType}", entityType.Name, idType.Name);

                // Check if we have a registered repository
                if (_registeredRepositories.TryGetValue(key, out var registeredType))
                {
                    var repository = (_IBaseRepository<TEntity, TId>)ActivatorUtilities.CreateInstance(_serviceProvider, registeredType);
                    ConfigureRepository(repository, options);
                    TrackRepositoryCreation(key, stopwatch.ElapsedMilliseconds);
                    return repository;
                }

                // Create a default repository
                var defaultRepository = new _BaseRepository<TEntity, TId>(_serviceProvider, _logger);
                ConfigureRepository(defaultRepository, options);
                TrackRepositoryCreation(key, stopwatch.ElapsedMilliseconds);
                return defaultRepository;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create repository for {EntityType}", entityType.Name);
                TrackFailedCreation(key);
                throw;
            }
        }

        public void RegisterRepository<TEntity, TId, TRepository>()
            where TEntity : class
            where TId : struct
            where TRepository : class, _IBaseRepository<TEntity, TId>
        {
            var entityType = typeof(TEntity);
            var idType = typeof(TId);
            var key = $"{entityType.Name}_{idType.Name}";

            _registeredRepositories.TryAdd(key, typeof(TRepository));
            _logger.LogInformation("Registered repository {RepositoryType} for {EntityType}", typeof(TRepository).Name, entityType.Name);
        }

        public void RegisterReadOnlyRepository<TEntity, TId, TRepository>()
            where TEntity : class
            where TId : struct
            where TRepository : class, _IReadOnlyRepository<TEntity, TId>
        {
            var entityType = typeof(TEntity);
            var idType = typeof(TId);
            var key = $"ReadOnly_{entityType.Name}_{idType.Name}";

            _registeredRepositories.TryAdd(key, typeof(TRepository));
            _logger.LogInformation("Registered read-only repository {RepositoryType} for {EntityType}", typeof(TRepository).Name, entityType.Name);
        }

        public Dictionary<string, Type> GetRegisteredRepositories()
        {
            return _registeredRepositories.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        }

        public bool CanCreateRepository<TEntity, TId>()
            where TEntity : class
            where TId : struct
        {
            var entityType = typeof(TEntity);
            var idType = typeof(TId);
            var key = $"{entityType.Name}_{idType.Name}";

            return _registeredRepositories.ContainsKey(key);
        }

        public void DisposeAllRepositories()
        {
            _logger.LogInformation("Disposing all active repositories");

            foreach (var repository in _activeRepositories.Values)
            {
                if (repository is IDisposable disposable)
                {
                    try
                    {
                        disposable.Dispose();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error disposing repository {RepositoryType}", repository.GetType().Name);
                    }
                }
            }

            _activeRepositories.Clear();
        }

        public _RepositoryFactoryStatistics GetStatistics()
        {
            lock (_statisticsLock)
            {
                var recentCreationTimes = _creationTimes.Take(100).ToArray();
                var averageCreationTime = recentCreationTimes.Length > 0 ? recentCreationTimes.Average() : 0;

                return new _RepositoryFactoryStatistics
                {
                    TotalRepositoriesCreated = _repositoryTypeCounts.Values.Sum(),
                    ActiveRepositories = _activeRepositories.Count,
                    DisposedRepositories = _repositoryTypeCounts.Values.Sum() - _activeRepositories.Count,
                    AverageCreationTimeMs = averageCreationTime,
                    TotalCreationTimeMs = recentCreationTimes.Sum(),
                    FailedCreations = 0, // Would need to track this separately
                    RepositoryTypeCounts = new Dictionary<string, int>(_repositoryTypeCounts),
                    LastUpdated = DateTime.UtcNow
                };
            }
        }

        private void ConfigureRepository<TEntity, TId>(_IBaseRepository<TEntity, TId> repository, _RepositoryFactoryOptions options)
            where TEntity : class
            where TId : struct
        {
            if (repository is _BaseRepository<TEntity, TId> baseRepository)
            {
                baseRepository.Configure(options);
            }
        }

        private void TrackRepositoryCreation(string key, long creationTimeMs)
        {
            _repositoryTypeCounts.AddOrUpdate(key, 1, (k, v) => v + 1);
            _creationTimes.Enqueue(creationTimeMs);

            // Keep only the last 1000 creation times to prevent memory leaks
            while (_creationTimes.Count > 1000)
            {
                _creationTimes.TryDequeue(out _);
            }

            _logger.LogDebug("Repository created in {CreationTime}ms", creationTimeMs);
        }

        private void TrackFailedCreation(string key)
        {
            _logger.LogWarning("Failed to create repository for key: {Key}", key);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                DisposeAllRepositories();
                _disposed = true;
                _logger.LogInformation("Repository factory disposed");
            }
        }
    }

    /// <summary>
    /// Read-only repository implementation for entities that don't need write operations
    /// </summary>
    public class _ReadOnlyRepository<TEntity, TId> : _IReadOnlyRepository<TEntity, TId>
        where TEntity : class
        where TId : struct
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger _logger;

        public _ReadOnlyRepository(IServiceProvider serviceProvider, ILogger logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        public Task<TEntity?> GetByIdAsync(TId id)
        {
            _logger.LogDebug("Getting entity by ID: {Id}", id);
            // Implementation would use DbContext to get entity
            return Task.FromResult<TEntity?>(null);
        }

        public Task<List<TEntity>> GetAllAsync()
        {
            _logger.LogDebug("Getting all entities");
            // Implementation would use DbContext to get all entities
            return Task.FromResult(new List<TEntity>());
        }

        public Task<bool> ExistsAsync(TId id)
        {
            _logger.LogDebug("Checking if entity exists: {Id}", id);
            // Implementation would use DbContext to check existence
            return Task.FromResult(false);
        }

        public Task<(List<TEntity> Items, int TotalCount, int Page, int PageSize)> GetPagedAsync(int page, int pageSize)
        {
            _logger.LogDebug("Getting paged entities: Page {Page}, Size {PageSize}", page, pageSize);
            // Implementation would use DbContext to get paged entities
            return Task.FromResult((new List<TEntity>(), 0, page, pageSize));
        }

        public Task<List<TEntity>> SearchAsync(string searchTerm)
        {
            _logger.LogDebug("Searching entities with term: {SearchTerm}", searchTerm);
            // Implementation would use DbContext to search entities
            return Task.FromResult(new List<TEntity>());
        }
    }
} 
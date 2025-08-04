// File: Nom.Api/_Abstractions/_Core/IBaseRepository.cs

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Nom.Api.Core
{
    /// <summary>
    /// Base repository interface for data access patterns
    /// </summary>
    /// <typeparam name="TEntity">The entity type</typeparam>
    /// <typeparam name="TId">The ID type</typeparam>
    public interface IBaseRepository<TEntity, TId> where TEntity : class
    {
        /// <summary>
        /// Gets the DbSet for the entity
        /// </summary>
        DbSet<TEntity> EntitySet { get; }

        /// <summary>
        /// Gets an entity by ID
        /// </summary>
        /// <param name="id">The entity ID</param>
        /// <returns>The entity or null if not found</returns>
        Task<TEntity?> GetByIdAsync(TId id);

        /// <summary>
        /// Gets an entity by ID with includes
        /// </summary>
        /// <param name="id">The entity ID</param>
        /// <param name="includes">The includes to apply</param>
        /// <returns>The entity or null if not found</returns>
        Task<TEntity?> GetByIdAsync(TId id, params Expression<Func<TEntity, object>>[] includes);

        /// <summary>
        /// Gets all entities
        /// </summary>
        /// <returns>All entities</returns>
        Task<IEnumerable<TEntity>> GetAllAsync();

        /// <summary>
        /// Gets all entities with includes
        /// </summary>
        /// <param name="includes">The includes to apply</param>
        /// <returns>All entities</returns>
        Task<IEnumerable<TEntity>> GetAllAsync(params Expression<Func<TEntity, object>>[] includes);

        /// <summary>
        /// Gets entities by predicate
        /// </summary>
        /// <param name="predicate">The predicate to filter by</param>
        /// <returns>Filtered entities</returns>
        Task<IEnumerable<TEntity>> GetByPredicateAsync(Expression<Func<TEntity, bool>> predicate);

        /// <summary>
        /// Gets entities by predicate with includes
        /// </summary>
        /// <param name="predicate">The predicate to filter by</param>
        /// <param name="includes">The includes to apply</param>
        /// <returns>Filtered entities</returns>
        Task<IEnumerable<TEntity>> GetByPredicateAsync(Expression<Func<TEntity, bool>> predicate, params Expression<Func<TEntity, object>>[] includes);

        /// <summary>
        /// Gets the first entity by predicate
        /// </summary>
        /// <param name="predicate">The predicate to filter by</param>
        /// <returns>The first entity or null if not found</returns>
        Task<TEntity?> GetFirstByPredicateAsync(Expression<Func<TEntity, bool>> predicate);

        /// <summary>
        /// Gets the first entity by predicate with includes
        /// </summary>
        /// <param name="predicate">The predicate to filter by</param>
        /// <param name="includes">The includes to apply</param>
        /// <returns>The first entity or null if not found</returns>
        Task<TEntity?> GetFirstByPredicateAsync(Expression<Func<TEntity, bool>> predicate, params Expression<Func<TEntity, object>>[] includes);

        /// <summary>
        /// Gets the count of entities by predicate
        /// </summary>
        /// <param name="predicate">The predicate to filter by</param>
        /// <returns>The count</returns>
        Task<int> GetCountAsync(Expression<Func<TEntity, bool>> predicate);

        /// <summary>
        /// Gets the count of all entities
        /// </summary>
        /// <returns>The count</returns>
        Task<int> GetCountAsync();

        /// <summary>
        /// Checks if any entity exists by predicate
        /// </summary>
        /// <param name="predicate">The predicate to check</param>
        /// <returns>True if any entity exists</returns>
        Task<bool> ExistsAsync(Expression<Func<TEntity, bool>> predicate);

        /// <summary>
        /// Checks if an entity exists by ID
        /// </summary>
        /// <param name="id">The entity ID</param>
        /// <returns>True if the entity exists</returns>
        Task<bool> ExistsAsync(TId id);

        /// <summary>
        /// Adds an entity
        /// </summary>
        /// <param name="entity">The entity to add</param>
        void Add(TEntity entity);

        /// <summary>
        /// Adds multiple entities
        /// </summary>
        /// <param name="entities">The entities to add</param>
        void AddRange(IEnumerable<TEntity> entities);

        /// <summary>
        /// Updates an entity
        /// </summary>
        /// <param name="entity">The entity to update</param>
        void Update(TEntity entity);

        /// <summary>
        /// Updates multiple entities
        /// </summary>
        /// <param name="entities">The entities to update</param>
        void UpdateRange(IEnumerable<TEntity> entities);

        /// <summary>
        /// Removes an entity
        /// </summary>
        /// <param name="entity">The entity to remove</param>
        void Remove(TEntity entity);

        /// <summary>
        /// Removes multiple entities
        /// </summary>
        /// <param name="entities">The entities to remove</param>
        void RemoveRange(IEnumerable<TEntity> entities);

        /// <summary>
        /// Removes entities by predicate
        /// </summary>
        /// <param name="predicate">The predicate to filter by</param>
        void RemoveByPredicate(Expression<Func<TEntity, bool>> predicate);

        /// <summary>
        /// Saves changes to the database
        /// </summary>
        /// <returns>The number of affected rows</returns>
        Task<int> SaveChangesAsync();

        /// <summary>
        /// Gets a queryable for the entity set
        /// </summary>
        /// <returns>The queryable</returns>
        IQueryable<TEntity> GetQueryable();

        /// <summary>
        /// Gets a queryable for the entity set with includes
        /// </summary>
        /// <param name="includes">The includes to apply</param>
        /// <returns>The queryable</returns>
        IQueryable<TEntity> GetQueryable(params Expression<Func<TEntity, object>>[] includes);

        /// <summary>
        /// Gets entities with pagination
        /// </summary>
        /// <param name="pageNumber">The page number (1-based)</param>
        /// <param name="pageSize">The page size</param>
        /// <returns>Paginated entities</returns>
        Task<PaginatedResult<TEntity>> GetPaginatedAsync(int pageNumber, int pageSize);

        /// <summary>
        /// Gets entities with pagination and predicate
        /// </summary>
        /// <param name="predicate">The predicate to filter by</param>
        /// <param name="pageNumber">The page number (1-based)</param>
        /// <param name="pageSize">The page size</param>
        /// <returns>Paginated entities</returns>
        Task<PaginatedResult<TEntity>> GetPaginatedAsync(Expression<Func<TEntity, bool>> predicate, int pageNumber, int pageSize);

        /// <summary>
        /// Gets entities with pagination, predicate, and includes
        /// </summary>
        /// <param name="predicate">The predicate to filter by</param>
        /// <param name="pageNumber">The page number (1-based)</param>
        /// <param name="pageSize">The page size</param>
        /// <param name="includes">The includes to apply</param>
        /// <returns>Paginated entities</returns>
        Task<PaginatedResult<TEntity>> GetPaginatedAsync(Expression<Func<TEntity, bool>> predicate, int pageNumber, int pageSize, params Expression<Func<TEntity, object>>[] includes);
    }
}
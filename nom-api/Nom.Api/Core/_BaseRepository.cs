// File: Nom.Api/_Abstractions/_Core/BaseRepository.cs

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Nom.Api.Core
{
    /// <summary>
    /// Base repository implementation for data access patterns
    /// </summary>
    /// <typeparam name="TEntity">The entity type</typeparam>
    /// <typeparam name="TId">The ID type</typeparam>
    public abstract class BaseRepository<TEntity, TId> : IBaseRepository<TEntity, TId> where TEntity : class
    {
        protected readonly DbContext _dbContext;
        protected readonly ILogger<BaseRepository<TEntity, TId>> _logger;

        protected BaseRepository(DbContext dbContext, ILogger<BaseRepository<TEntity, TId>> logger)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public abstract DbSet<TEntity> EntitySet { get; }

        public virtual async Task<TEntity?> GetByIdAsync(TId id)
        {
            try
            {
                _logger.LogDebug("Getting entity by ID: {Id}", id);
                return await EntitySet.FindAsync(id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting entity by ID: {Id}", id);
                throw;
            }
        }

        public virtual async Task<TEntity?> GetByIdAsync(TId id, params Expression<Func<TEntity, object>>[] includes)
        {
            try
            {
                _logger.LogDebug("Getting entity by ID with includes: {Id}", id);
                var query = EntitySet.AsQueryable();

                foreach (var include in includes)
                {
                    query = query.Include(include);
                }

                return await query.FirstOrDefaultAsync(e => GetEntityId(e).Equals(id));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting entity by ID with includes: {Id}", id);
                throw;
            }
        }

        public virtual async Task<IEnumerable<TEntity>> GetAllAsync()
        {
            try
            {
                _logger.LogDebug("Getting all entities");
                return await EntitySet.ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting all entities");
                throw;
            }
        }

        public virtual async Task<IEnumerable<TEntity>> GetAllAsync(params Expression<Func<TEntity, object>>[] includes)
        {
            try
            {
                _logger.LogDebug("Getting all entities with includes");
                var query = EntitySet.AsQueryable();

                foreach (var include in includes)
                {
                    query = query.Include(include);
                }

                return await query.ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting all entities with includes");
                throw;
            }
        }

        public virtual async Task<IEnumerable<TEntity>> GetByPredicateAsync(Expression<Func<TEntity, bool>> predicate)
        {
            try
            {
                _logger.LogDebug("Getting entities by predicate");
                return await EntitySet.Where(predicate).ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting entities by predicate");
                throw;
            }
        }

        public virtual async Task<IEnumerable<TEntity>> GetByPredicateAsync(Expression<Func<TEntity, bool>> predicate, params Expression<Func<TEntity, object>>[] includes)
        {
            try
            {
                _logger.LogDebug("Getting entities by predicate with includes");
                var query = EntitySet.Where(predicate);

                foreach (var include in includes)
                {
                    query = query.Include(include);
                }

                return await query.ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting entities by predicate with includes");
                throw;
            }
        }

        public virtual async Task<TEntity?> GetFirstByPredicateAsync(Expression<Func<TEntity, bool>> predicate)
        {
            try
            {
                _logger.LogDebug("Getting first entity by predicate");
                return await EntitySet.FirstOrDefaultAsync(predicate);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting first entity by predicate");
                throw;
            }
        }

        public virtual async Task<TEntity?> GetFirstByPredicateAsync(Expression<Func<TEntity, bool>> predicate, params Expression<Func<TEntity, object>>[] includes)
        {
            try
            {
                _logger.LogDebug("Getting first entity by predicate with includes");
                var query = EntitySet.Where(predicate);

                foreach (var include in includes)
                {
                    query = query.Include(include);
                }

                return await query.FirstOrDefaultAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting first entity by predicate with includes");
                throw;
            }
        }

        public virtual async Task<int> GetCountAsync(Expression<Func<TEntity, bool>> predicate)
        {
            try
            {
                _logger.LogDebug("Getting count by predicate");
                return await EntitySet.CountAsync(predicate);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting count by predicate");
                throw;
            }
        }

        public virtual async Task<int> GetCountAsync()
        {
            try
            {
                _logger.LogDebug("Getting total count");
                return await EntitySet.CountAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting total count");
                throw;
            }
        }

        public virtual async Task<bool> ExistsAsync(Expression<Func<TEntity, bool>> predicate)
        {
            try
            {
                _logger.LogDebug("Checking existence by predicate");
                return await EntitySet.AnyAsync(predicate);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking existence by predicate");
                throw;
            }
        }

        public virtual async Task<bool> ExistsAsync(TId id)
        {
            try
            {
                _logger.LogDebug("Checking existence by ID: {Id}", id);
                return await EntitySet.AnyAsync(e => GetEntityId(e).Equals(id));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking existence by ID: {Id}", id);
                throw;
            }
        }

        public virtual void Add(TEntity entity)
        {
            try
            {
                _logger.LogDebug("Adding entity");
                EntitySet.Add(entity);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding entity");
                throw;
            }
        }

        public virtual void AddRange(IEnumerable<TEntity> entities)
        {
            try
            {
                _logger.LogDebug("Adding range of entities");
                EntitySet.AddRange(entities);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding range of entities");
                throw;
            }
        }

        public virtual void Update(TEntity entity)
        {
            try
            {
                _logger.LogDebug("Updating entity");
                EntitySet.Update(entity);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating entity");
                throw;
            }
        }

        public virtual void UpdateRange(IEnumerable<TEntity> entities)
        {
            try
            {
                _logger.LogDebug("Updating range of entities");
                EntitySet.UpdateRange(entities);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating range of entities");
                throw;
            }
        }

        public virtual void Remove(TEntity entity)
        {
            try
            {
                _logger.LogDebug("Removing entity");
                EntitySet.Remove(entity);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing entity");
                throw;
            }
        }

        public virtual void RemoveRange(IEnumerable<TEntity> entities)
        {
            try
            {
                _logger.LogDebug("Removing range of entities");
                EntitySet.RemoveRange(entities);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing range of entities");
                throw;
            }
        }

        public virtual void RemoveByPredicate(Expression<Func<TEntity, bool>> predicate)
        {
            try
            {
                _logger.LogDebug("Removing entities by predicate");
                var entitiesToRemove = EntitySet.Where(predicate);
                EntitySet.RemoveRange(entitiesToRemove);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing entities by predicate");
                throw;
            }
        }

        public virtual async Task<int> SaveChangesAsync()
        {
            try
            {
                _logger.LogDebug("Saving changes");
                return await _dbContext.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving changes");
                throw;
            }
        }

        public virtual IQueryable<TEntity> GetQueryable()
        {
            return EntitySet.AsQueryable();
        }

        public virtual IQueryable<TEntity> GetQueryable(params Expression<Func<TEntity, object>>[] includes)
        {
            var query = EntitySet.AsQueryable();

            foreach (var include in includes)
            {
                query = query.Include(include);
            }

            return query;
        }

        public virtual async Task<PaginatedResult<TEntity>> GetPaginatedAsync(int pageNumber, int pageSize)
        {
            try
            {
                _logger.LogDebug("Getting paginated entities: Page {PageNumber}, Size {PageSize}", pageNumber, pageSize);

                var totalCount = await GetCountAsync();
                var items = await EntitySet
                    .Skip((pageNumber - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                return new PaginatedResult<TEntity>
                {
                    Items = items,
                    TotalCount = totalCount,
                    PageNumber = pageNumber,
                    PageSize = pageSize
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting paginated entities");
                throw;
            }
        }

        public virtual async Task<PaginatedResult<TEntity>> GetPaginatedAsync(Expression<Func<TEntity, bool>> predicate, int pageNumber, int pageSize)
        {
            try
            {
                _logger.LogDebug("Getting paginated entities by predicate: Page {PageNumber}, Size {PageSize}", pageNumber, pageSize);

                var totalCount = await GetCountAsync(predicate);
                var items = await EntitySet
                    .Where(predicate)
                    .Skip((pageNumber - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                return new PaginatedResult<TEntity>
                {
                    Items = items,
                    TotalCount = totalCount,
                    PageNumber = pageNumber,
                    PageSize = pageSize
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting paginated entities by predicate");
                throw;
            }
        }

        public virtual async Task<PaginatedResult<TEntity>> GetPaginatedAsync(Expression<Func<TEntity, bool>> predicate, int pageNumber, int pageSize, params Expression<Func<TEntity, object>>[] includes)
        {
            try
            {
                _logger.LogDebug("Getting paginated entities by predicate with includes: Page {PageNumber}, Size {PageSize}", pageNumber, pageSize);

                var query = EntitySet.Where(predicate);

                foreach (var include in includes)
                {
                    query = query.Include(include);
                }

                var totalCount = await query.CountAsync();
                var items = await query
                    .Skip((pageNumber - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                return new PaginatedResult<TEntity>
                {
                    Items = items,
                    TotalCount = totalCount,
                    PageNumber = pageNumber,
                    PageSize = pageSize
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting paginated entities by predicate with includes");
                throw;
            }
        }

        /// <summary>
        /// Gets the ID of an entity
        /// </summary>
        /// <param name="entity">The entity</param>
        /// <returns>The entity ID</returns>
        protected abstract TId GetEntityId(TEntity entity);
    }
}
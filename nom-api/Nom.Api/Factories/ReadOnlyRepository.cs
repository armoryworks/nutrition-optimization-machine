using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Nom.Api.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Nom.Data;

namespace Nom.Api.Factories
{
    /// <summary>
    /// Read-only repository implementation for entities that don't need write operations
    /// </summary>
    public class ReadOnlyRepository<TEntity, TId> : IReadOnlyRepository<TEntity, TId>
        where TEntity : class
        where TId : struct
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<ReadOnlyRepository<TEntity, TId>> _logger;
        private ApplicationDbContext? _context;

        public DbSet<TEntity> EntitySet => _context!.Set<TEntity>();

        public ReadOnlyRepository(IServiceProvider serviceProvider, ILogger<ReadOnlyRepository<TEntity, TId>> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        private ApplicationDbContext GetContext()
        {
            if (_context == null)
            {
                _context = _serviceProvider.GetRequiredService<ApplicationDbContext>();
            }
            return _context;
        }

        public async Task<TEntity?> GetByIdAsync(TId id)
        {
            try
            {
                _logger.LogDebug("Getting entity by ID: {Id}", id);
                return await GetContext().Set<TEntity>().FindAsync(id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting entity by ID: {Id}", id);
                return null;
            }
        }

        public async Task<TEntity?> GetByIdAsync(TId id, params Expression<Func<TEntity, object>>[] includes)
        {
            try
            {
                _logger.LogDebug("Getting entity by ID with includes: {Id}", id);
                var query = GetContext().Set<TEntity>().AsQueryable();
                foreach (var include in includes)
                {
                    query = query.Include(include);
                }
                return await query.FirstOrDefaultAsync(e => EF.Property<TId>(e, "Id").Equals(id));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting entity by ID with includes: {Id}", id);
                return null;
            }
        }

        public async Task<List<TEntity>> GetAllAsync()
        {
            try
            {
                _logger.LogDebug("Getting all entities");
                return await GetContext().Set<TEntity>().ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting all entities");
                return new List<TEntity>();
            }
        }

        public async Task<List<TEntity>> GetAllAsync(params Expression<Func<TEntity, object>>[] includes)
        {
            try
            {
                _logger.LogDebug("Getting all entities with includes");
                var query = GetContext().Set<TEntity>().AsQueryable();
                foreach (var include in includes)
                {
                    query = query.Include(include);
                }
                return await query.ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting all entities with includes");
                return new List<TEntity>();
            }
        }

        public async Task<List<TEntity>> GetByPredicateAsync(Expression<Func<TEntity, bool>> predicate)
        {
            try
            {
                _logger.LogDebug("Getting entities by predicate");
                return await GetContext().Set<TEntity>().Where(predicate).ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting entities by predicate");
                return new List<TEntity>();
            }
        }

        public async Task<List<TEntity>> GetByPredicateAsync(Expression<Func<TEntity, bool>> predicate, params Expression<Func<TEntity, object>>[] includes)
        {
            try
            {
                _logger.LogDebug("Getting entities by predicate with includes");
                var query = GetContext().Set<TEntity>().Where(predicate);
                foreach (var include in includes)
                {
                    query = query.Include(include);
                }
                return await query.ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting entities by predicate with includes");
                return new List<TEntity>();
            }
        }

        public async Task<TEntity?> GetFirstByPredicateAsync(Expression<Func<TEntity, bool>> predicate)
        {
            try
            {
                _logger.LogDebug("Getting first entity by predicate");
                return await GetContext().Set<TEntity>().FirstOrDefaultAsync(predicate);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting first entity by predicate");
                return null;
            }
        }

        public async Task<TEntity?> GetFirstByPredicateAsync(Expression<Func<TEntity, bool>> predicate, params Expression<Func<TEntity, object>>[] includes)
        {
            try
            {
                _logger.LogDebug("Getting first entity by predicate with includes");
                var query = GetContext().Set<TEntity>().AsQueryable();
                foreach (var include in includes)
                {
                    query = query.Include(include);
                }
                return await query.FirstOrDefaultAsync(predicate);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting first entity by predicate with includes");
                return null;
            }
        }

        public async Task<int> GetCountAsync(Expression<Func<TEntity, bool>> predicate)
        {
            try
            {
                _logger.LogDebug("Getting count by predicate");
                return await GetContext().Set<TEntity>().CountAsync(predicate);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting count by predicate");
                return 0;
            }
        }

        public async Task<int> GetCountAsync()
        {
            try
            {
                _logger.LogDebug("Getting total count");
                return await GetContext().Set<TEntity>().CountAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting total count");
                return 0;
            }
        }

        public async Task<bool> ExistsAsync(Expression<Func<TEntity, bool>> predicate)
        {
            try
            {
                _logger.LogDebug("Checking if entity exists by predicate");
                return await GetContext().Set<TEntity>().AnyAsync(predicate);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking if entity exists by predicate");
                return false;
            }
        }

        public async Task<bool> ExistsAsync(TId id)
        {
            try
            {
                _logger.LogDebug("Checking if entity exists by ID: {Id}", id);
                return await GetContext().Set<TEntity>().FindAsync(id) != null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking if entity exists by ID: {Id}", id);
                return false;
            }
        }

        public IQueryable<TEntity> GetQueryable()
        {
            return GetContext().Set<TEntity>().AsQueryable();
        }

        public IQueryable<TEntity> GetQueryable(params Expression<Func<TEntity, object>>[] includes)
        {
            var query = GetContext().Set<TEntity>().AsQueryable();
            foreach (var include in includes)
            {
                query = query.Include(include);
            }
            return query;
        }

        public async Task<PaginatedResult<TEntity>> GetPaginatedAsync(int pageNumber, int pageSize)
        {
            try
            {
                _logger.LogDebug("Getting paginated entities: Page {PageNumber}, Size {PageSize}", pageNumber, pageSize);
                var entitySet = GetContext().Set<TEntity>();
                var totalCount = await entitySet.CountAsync();
                var entities = await entitySet
                    .Skip((pageNumber - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                return new PaginatedResult<TEntity>
                {
                    Items = entities,
                    TotalCount = totalCount,
                    PageNumber = pageNumber,
                    PageSize = pageSize
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting paginated entities");
                return new PaginatedResult<TEntity>
                {
                    Items = new List<TEntity>(),
                    TotalCount = 0,
                    PageNumber = pageNumber,
                    PageSize = pageSize
                };
            }
        }

        public async Task<PaginatedResult<TEntity>> GetPaginatedAsync(Expression<Func<TEntity, bool>> predicate, int pageNumber, int pageSize)
        {
            try
            {
                _logger.LogDebug("Getting paginated entities by predicate: Page {PageNumber}, Size {PageSize}", pageNumber, pageSize);
                var query = GetContext().Set<TEntity>().Where(predicate);
                var totalCount = await query.CountAsync();
                var entities = await query
                    .Skip((pageNumber - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                return new PaginatedResult<TEntity>
                {
                    Items = entities,
                    TotalCount = totalCount,
                    PageNumber = pageNumber,
                    PageSize = pageSize
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting paginated entities by predicate");
                return new PaginatedResult<TEntity>
                {
                    Items = new List<TEntity>(),
                    TotalCount = 0,
                    PageNumber = pageNumber,
                    PageSize = pageSize
                };
            }
        }

        public async Task<PaginatedResult<TEntity>> GetPaginatedAsync(Expression<Func<TEntity, bool>> predicate, int pageNumber, int pageSize, params Expression<Func<TEntity, object>>[] includes)
        {
            try
            {
                _logger.LogDebug("Getting paginated entities by predicate with includes: Page {PageNumber}, Size {PageSize}", pageNumber, pageSize);
                var query = GetContext().Set<TEntity>().Where(predicate);
                foreach (var include in includes)
                {
                    query = query.Include(include);
                }
                var totalCount = await query.CountAsync();
                var entities = await query
                    .Skip((pageNumber - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                return new PaginatedResult<TEntity>
                {
                    Items = entities,
                    TotalCount = totalCount,
                    PageNumber = pageNumber,
                    PageSize = pageSize
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting paginated entities by predicate with includes");
                return new PaginatedResult<TEntity>
                {
                    Items = new List<TEntity>(),
                    TotalCount = 0,
                    PageNumber = pageNumber,
                    PageSize = pageSize
                };
            }
        }

        public async Task<List<TEntity>> SearchAsync(string searchTerm)
        {
            try
            {
                _logger.LogDebug("Searching entities with term: {SearchTerm}", searchTerm);
                // This is a basic implementation - in a real app, you'd implement proper search logic
                return await GetContext().Set<TEntity>().ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching entities");
                return new List<TEntity>();
            }
        }

        public async Task<List<TEntity>> SearchAsync(string searchTerm, params Expression<Func<TEntity, object>>[] includes)
        {
            try
            {
                _logger.LogDebug("Searching entities with term and includes: {SearchTerm}", searchTerm);
                var query = GetContext().Set<TEntity>().AsQueryable();
                foreach (var include in includes)
                {
                    query = query.Include(include);
                }
                // This is a basic implementation - in a real app, you'd implement proper search logic
                return await query.ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching entities with includes");
                return new List<TEntity>();
            }
        }

        public async Task<List<TEntity>> GetOrderedAsync<TKey>(Expression<Func<TEntity, TKey>> keySelector, bool ascending = true)
        {
            try
            {
                _logger.LogDebug("Getting ordered entities");
                var query = ascending ? GetContext().Set<TEntity>().OrderBy(keySelector) : GetContext().Set<TEntity>().OrderByDescending(keySelector);
                return await query.ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting ordered entities");
                return new List<TEntity>();
            }
        }

        public async Task<List<TEntity>> GetOrderedAsync<TKey>(Expression<Func<TEntity, TKey>> keySelector, bool ascending, params Expression<Func<TEntity, object>>[] includes)
        {
            try
            {
                _logger.LogDebug("Getting ordered entities with includes");
                var query = GetContext().Set<TEntity>().AsQueryable();
                foreach (var include in includes)
                {
                    query = query.Include(include);
                }
                query = ascending ? query.OrderBy(keySelector) : query.OrderByDescending(keySelector);
                return await query.ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting ordered entities with includes");
                return new List<TEntity>();
            }
        }
    }
}
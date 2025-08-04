using Microsoft.EntityFrameworkCore;
using Nom.Api.Factories.Interfaces;
using Nom.Data;

namespace Nom.Api.Factories
{
    /// <summary>
    /// Read-only orchestration service implementation
    /// </summary>
    /// <typeparam name="TEntity">The entity type</typeparam>
    /// <typeparam name="TResponseModel">The response model type</typeparam>
    /// <typeparam name="TId">The ID type</typeparam>
    public class ReadOnlyOrchestrationService<TEntity, TResponseModel, TId>
        : IReadOnlyOrchestrationService<TEntity, TResponseModel, TId>
        where TEntity : class
        where TResponseModel : class
        where TId : struct
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ILogger _logger;

        public ReadOnlyOrchestrationService(
            ApplicationDbContext dbContext,
            IHttpContextAccessor httpContextAccessor,
            ILogger logger)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public ILogger Logger => _logger;

        public async Task<TResponseModel?> GetByIdAsync(TId id)
        {
            try
            {
                _logger.LogDebug("Getting entity {EntityId} of type {EntityType}", id, typeof(TEntity).Name);

                var entity = await _dbContext.Set<TEntity>().FindAsync(id);
                if (entity == null)
                {
                    _logger.LogDebug("Entity {EntityId} of type {EntityType} not found", id, typeof(TEntity).Name);
                    return null;
                }

                return MapToResponseModel(entity);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting entity {EntityId} of type {EntityType}", id, typeof(TEntity).Name);
                throw;
            }
        }

        public async Task<List<TResponseModel>> GetAllAsync()
        {
            try
            {
                _logger.LogDebug("Getting all entities of type {EntityType}", typeof(TEntity).Name);

                var entities = await _dbContext.Set<TEntity>().ToListAsync();
                return entities.Select(MapToResponseModel).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting all entities of type {EntityType}", typeof(TEntity).Name);
                throw;
            }
        }

        public async Task<(List<TResponseModel> Items, int TotalCount, int Page, int PageSize)> GetPagedAsync(int page, int pageSize)
        {
            try
            {
                _logger.LogDebug("Getting paged entities of type {EntityType}, page {Page}, size {PageSize}",
                    typeof(TEntity).Name, page, pageSize);

                var totalCount = await _dbContext.Set<TEntity>().CountAsync();
                var skip = (page - 1) * pageSize;

                var entities = await _dbContext.Set<TEntity>()
                    .Skip(skip)
                    .Take(pageSize)
                    .ToListAsync();

                var items = entities.Select(MapToResponseModel).ToList();

                return (items, totalCount, page, pageSize);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting paged entities of type {EntityType}", typeof(TEntity).Name);
                throw;
            }
        }

        public async Task<bool> ExistsAsync(TId id)
        {
            try
            {
                return await _dbContext.Set<TEntity>().FindAsync(id) != null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking existence of entity {EntityId} of type {EntityType}", id, typeof(TEntity).Name);
                throw;
            }
        }

        /// <summary>
        /// Maps an entity to a response model
        /// </summary>
        /// <param name="entity">The entity to map</param>
        /// <returns>The mapped response model</returns>
        protected virtual TResponseModel MapToResponseModel(TEntity entity)
        {
            // Default implementation - override in derived classes for specific mapping
            return (TResponseModel)Convert.ChangeType(entity, typeof(TResponseModel));
        }
    }
}
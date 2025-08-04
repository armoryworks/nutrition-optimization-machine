namespace Nom.Api.Factories.Interfaces
{
    /// <summary>
    /// Base interface for orchestration services that don't need all CRUD operations
    /// </summary>
    /// <typeparam name="TEntity">The entity type this service manages</typeparam>
    /// <typeparam name="TResponseModel">The model returned in responses</typeparam>
    /// <typeparam name="TId">The type of the entity's primary key</typeparam>
    public interface IReadOnlyOrchestrationService<TEntity, TResponseModel, TId>
        where TEntity : class
        where TResponseModel : class
        where TId : struct
    {
        /// <summary>
        /// Gets the logger instance for this service
        /// </summary>
        ILogger Logger { get; }

        /// <summary>
        /// Gets an entity by its ID
        /// </summary>
        /// <param name="id">The entity ID</param>
        /// <returns>The entity if found, null otherwise</returns>
        Task<TResponseModel?> GetByIdAsync(TId id);

        /// <summary>
        /// Gets all entities
        /// </summary>
        /// <returns>List of all entities</returns>
        Task<List<TResponseModel>> GetAllAsync();

        /// <summary>
        /// Gets entities with pagination
        /// </summary>
        /// <param name="page">Page number (1-based)</param>
        /// <param name="pageSize">Number of items per page</param>
        /// <returns>Paginated list of entities</returns>
        Task<(List<TResponseModel> Items, int TotalCount, int Page, int PageSize)> GetPagedAsync(int page, int pageSize);

        /// <summary>
        /// Checks if an entity exists
        /// </summary>
        /// <param name="id">The entity ID</param>
        /// <returns>True if exists, false otherwise</returns>
        Task<bool> ExistsAsync(TId id);
    }
}
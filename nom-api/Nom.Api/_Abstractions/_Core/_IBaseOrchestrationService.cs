// File: Nom.Api/_Abstractions/_Core/_IBaseOrchestrationService.cs

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Nom.Api._Abstractions._Core
{
    /// <summary>
    /// Base interface for all orchestration services providing common CRUD operations
    /// and standardized patterns for business logic orchestration.
    /// </summary>
    /// <typeparam name="TEntity">The entity type this service manages</typeparam>
    /// <typeparam name="TCreateModel">The model used for creating entities</typeparam>
    /// <typeparam name="TUpdateModel">The model used for updating entities</typeparam>
    /// <typeparam name="TResponseModel">The model returned in responses</typeparam>
    /// <typeparam name="TId">The type of the entity's primary key</typeparam>
    public interface _IBaseOrchestrationService<TEntity, TCreateModel, TUpdateModel, TResponseModel, TId>
        where TEntity : class
        where TCreateModel : class
        where TUpdateModel : class
        where TResponseModel : class
        where TId : struct
    {
        /// <summary>
        /// Gets the logger instance for this service
        /// </summary>
        ILogger Logger { get; }

        /// <summary>
        /// Creates a new entity
        /// </summary>
        /// <param name="createModel">The model containing creation data</param>
        /// <returns>The created entity response</returns>
        Task<TResponseModel> CreateAsync(TCreateModel createModel);

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
        /// Updates an existing entity
        /// </summary>
        /// <param name="id">The entity ID</param>
        /// <param name="updateModel">The model containing update data</param>
        /// <returns>The updated entity response</returns>
        Task<TResponseModel> UpdateAsync(TId id, TUpdateModel updateModel);

        /// <summary>
        /// Deletes an entity
        /// </summary>
        /// <param name="id">The entity ID</param>
        /// <returns>True if deleted successfully, false otherwise</returns>
        Task<bool> DeleteAsync(TId id);

        /// <summary>
        /// Checks if an entity exists
        /// </summary>
        /// <param name="id">The entity ID</param>
        /// <returns>True if exists, false otherwise</returns>
        Task<bool> ExistsAsync(TId id);

        /// <summary>
        /// Gets entities with pagination
        /// </summary>
        /// <param name="page">Page number (1-based)</param>
        /// <param name="pageSize">Number of items per page</param>
        /// <returns>Paginated list of entities</returns>
        Task<(List<TResponseModel> Items, int TotalCount, int Page, int PageSize)> GetPagedAsync(int page, int pageSize);

        /// <summary>
        /// Validates a create model
        /// </summary>
        /// <param name="createModel">The model to validate</param>
        /// <returns>Validation result</returns>
        Task<(bool IsValid, List<string> Errors)> ValidateCreateAsync(TCreateModel createModel);

        /// <summary>
        /// Validates an update model
        /// </summary>
        /// <param name="id">The entity ID</param>
        /// <param name="updateModel">The model to validate</param>
        /// <returns>Validation result</returns>
        Task<(bool IsValid, List<string> Errors)> ValidateUpdateAsync(TId id, TUpdateModel updateModel);

        /// <summary>
        /// Handles business logic exceptions
        /// </summary>
        /// <param name="ex">The exception to handle</param>
        /// <param name="operation">The operation being performed</param>
        /// <param name="entityId">The entity ID (if applicable)</param>
        void HandleException(Exception ex, string operation, TId? entityId = null);
    }

    /// <summary>
    /// Base interface for orchestration services that don't need all CRUD operations
    /// </summary>
    /// <typeparam name="TEntity">The entity type this service manages</typeparam>
    /// <typeparam name="TResponseModel">The model returned in responses</typeparam>
    /// <typeparam name="TId">The type of the entity's primary key</typeparam>
    public interface _IReadOnlyOrchestrationService<TEntity, TResponseModel, TId>
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
// File: Nom.Api/_Abstractions/_Core/BaseOrchestrationService.cs

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nom.Data;
using Nom.Api.Core;

namespace Nom.Api.Core
{
    /// <summary>
    /// Base implementation for orchestration services providing common patterns
    /// and functionality for business logic orchestration.
    /// </summary>
    /// <typeparam name="TEntity">The entity type this service manages</typeparam>
    /// <typeparam name="TCreateModel">The model used for creating entities</typeparam>
    /// <typeparam name="TUpdateModel">The model used for updating entities</typeparam>
    /// <typeparam name="TResponseModel">The model returned in responses</typeparam>
    /// <typeparam name="TId">The type of the entity's primary key</typeparam>
    public abstract class BaseOrchestrationService<TEntity, TCreateModel, TUpdateModel, TResponseModel, TId>
        : IBaseOrchestrationService<TEntity, TCreateModel, TUpdateModel, TResponseModel, TId>
        where TEntity : class
        where TCreateModel : class
        where TUpdateModel : class
        where TResponseModel : class
        where TId : struct
    {
        protected readonly ApplicationDbContext _dbContext;
        protected readonly IHttpContextAccessor _httpContextAccessor;
        protected readonly ILogger _logger;

        protected BaseOrchestrationService(
            ApplicationDbContext dbContext,
            IHttpContextAccessor httpContextAccessor,
            ILogger logger)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public ILogger Logger => _logger;

        /// <summary>
        /// Gets the DbSet for the entity type
        /// </summary>
        protected abstract DbSet<TEntity> EntitySet { get; }

        /// <summary>
        /// Maps an entity to a response model
        /// </summary>
        /// <param name="entity">The entity to map</param>
        /// <returns>The mapped response model</returns>
        protected abstract TResponseModel MapToResponseModel(TEntity entity);

        /// <summary>
        /// Maps a create model to an entity
        /// </summary>
        /// <param name="createModel">The create model to map</param>
        /// <returns>The mapped entity</returns>
        protected abstract TEntity MapToEntity(TCreateModel createModel);

        /// <summary>
        /// Updates an entity with data from an update model
        /// </summary>
        /// <param name="entity">The entity to update</param>
        /// <param name="updateModel">The update model containing the new data</param>
        protected abstract void UpdateEntity(TEntity entity, TUpdateModel updateModel);

        /// <summary>
        /// Gets the primary key value from an entity
        /// </summary>
        /// <param name="entity">The entity</param>
        /// <returns>The primary key value</returns>
        protected abstract TId GetEntityId(TEntity entity);

        /// <summary>
        /// Sets the primary key value on an entity
        /// </summary>
        /// <param name="entity">The entity</param>
        /// <param name="id">The primary key value</param>
        protected abstract void SetEntityId(TEntity entity, TId id);

        /// <summary>
        /// Validates that an entity can be created
        /// </summary>
        /// <param name="createModel">The create model to validate</param>
        /// <returns>Validation result</returns>
        protected virtual async Task<(bool IsValid, List<string> Errors)> ValidateCreateInternalAsync(TCreateModel createModel)
        {
            var errors = new List<string>();

            if (createModel == null)
            {
                errors.Add("Create model cannot be null");
                return (false, errors);
            }

            // Override in derived classes for specific validation
            return (true, errors);
        }

        /// <summary>
        /// Validates that an entity can be updated
        /// </summary>
        /// <param name="id">The entity ID</param>
        /// <param name="updateModel">The update model to validate</param>
        /// <returns>Validation result</returns>
        protected virtual async Task<(bool IsValid, List<string> Errors)> ValidateUpdateInternalAsync(TId id, TUpdateModel updateModel)
        {
            var errors = new List<string>();

            if (updateModel == null)
            {
                errors.Add("Update model cannot be null");
                return (false, errors);
            }

            var entity = await EntitySet.FindAsync(id);
            if (entity == null)
            {
                errors.Add($"Entity with ID {id} not found");
                return (false, errors);
            }

            // Override in derived classes for specific validation
            return (true, errors);
        }

        /// <summary>
        /// Performs pre-creation business logic
        /// </summary>
        /// <param name="createModel">The create model</param>
        /// <param name="entity">The entity to be created</param>
        protected virtual async Task PreCreateAsync(TCreateModel createModel, TEntity entity)
        {
            // Override in derived classes for specific pre-creation logic
            await Task.CompletedTask;
        }

        /// <summary>
        /// Performs post-creation business logic
        /// </summary>
        /// <param name="entity">The created entity</param>
        /// <param name="createModel">The create model used</param>
        protected virtual async Task PostCreateAsync(TEntity entity, TCreateModel createModel)
        {
            // Override in derived classes for specific post-creation logic
            await Task.CompletedTask;
        }

        /// <summary>
        /// Performs pre-update business logic
        /// </summary>
        /// <param name="id">The entity ID</param>
        /// <param name="updateModel">The update model</param>
        /// <param name="entity">The entity to be updated</param>
        protected virtual async Task PreUpdateAsync(TId id, TUpdateModel updateModel, TEntity entity)
        {
            // Override in derived classes for specific pre-update logic
            await Task.CompletedTask;
        }

        /// <summary>
        /// Performs post-update business logic
        /// </summary>
        /// <param name="entity">The updated entity</param>
        /// <param name="updateModel">The update model used</param>
        protected virtual async Task PostUpdateAsync(TEntity entity, TUpdateModel updateModel)
        {
            // Override in derived classes for specific post-update logic
            await Task.CompletedTask;
        }

        /// <summary>
        /// Performs pre-delete business logic
        /// </summary>
        /// <param name="id">The entity ID</param>
        /// <param name="entity">The entity to be deleted</param>
        protected virtual async Task PreDeleteAsync(TId id, TEntity entity)
        {
            // Override in derived classes for specific pre-delete logic
            await Task.CompletedTask;
        }

        /// <summary>
        /// Performs post-delete business logic
        /// </summary>
        /// <param name="id">The entity ID</param>
        protected virtual async Task PostDeleteAsync(TId id)
        {
            // Override in derived classes for specific post-delete logic
            await Task.CompletedTask;
        }

        public virtual async Task<TResponseModel> CreateAsync(TCreateModel createModel)
        {
            try
            {
                _logger.LogInformation("Creating entity of type {EntityType}", typeof(TEntity).Name);

                var validation = await ValidateCreateInternalAsync(createModel);
                if (!validation.IsValid)
                {
                    throw new InvalidOperationException($"Validation failed: {string.Join(", ", validation.Errors)}");
                }

                var entity = MapToEntity(createModel);

                await PreCreateAsync(createModel, entity);

                EntitySet.Add(entity);
                await _dbContext.SaveChangesAsync();

                await PostCreateAsync(entity, createModel);

                var response = MapToResponseModel(entity);
                _logger.LogInformation("Successfully created entity {EntityId} of type {EntityType}",
                    GetEntityId(entity), typeof(TEntity).Name);

                return response;
            }
            catch (Exception ex)
            {
                HandleException(ex, "Create");
                throw;
            }
        }

        public virtual async Task<TResponseModel?> GetByIdAsync(TId id)
        {
            try
            {
                _logger.LogDebug("Getting entity {EntityId} of type {EntityType}", id, typeof(TEntity).Name);

                var entity = await EntitySet.FindAsync(id);
                if (entity == null)
                {
                    _logger.LogDebug("Entity {EntityId} of type {EntityType} not found", id, typeof(TEntity).Name);
                    return null;
                }

                return MapToResponseModel(entity);
            }
            catch (Exception ex)
            {
                HandleException(ex, "GetById", id);
                throw;
            }
        }

        public virtual async Task<List<TResponseModel>> GetAllAsync()
        {
            try
            {
                _logger.LogDebug("Getting all entities of type {EntityType}", typeof(TEntity).Name);

                var entities = await EntitySet.ToListAsync();
                return entities.Select(MapToResponseModel).ToList();
            }
            catch (Exception ex)
            {
                HandleException(ex, "GetAll");
                throw;
            }
        }

        public virtual async Task<TResponseModel> UpdateAsync(TId id, TUpdateModel updateModel)
        {
            try
            {
                _logger.LogInformation("Updating entity {EntityId} of type {EntityType}", id, typeof(TEntity).Name);

                var validation = await ValidateUpdateInternalAsync(id, updateModel);
                if (!validation.IsValid)
                {
                    throw new InvalidOperationException($"Validation failed: {string.Join(", ", validation.Errors)}");
                }

                var entity = await EntitySet.FindAsync(id);
                if (entity == null)
                {
                    throw new InvalidOperationException($"Entity with ID {id} not found");
                }

                await PreUpdateAsync(id, updateModel, entity);

                UpdateEntity(entity, updateModel);
                EntitySet.Update(entity);
                await _dbContext.SaveChangesAsync();

                await PostUpdateAsync(entity, updateModel);

                var response = MapToResponseModel(entity);
                _logger.LogInformation("Successfully updated entity {EntityId} of type {EntityType}",
                    id, typeof(TEntity).Name);

                return response;
            }
            catch (Exception ex)
            {
                HandleException(ex, "Update", id);
                throw;
            }
        }

        public virtual async Task<bool> DeleteAsync(TId id)
        {
            try
            {
                _logger.LogInformation("Deleting entity {EntityId} of type {EntityType}", id, typeof(TEntity).Name);

                var entity = await EntitySet.FindAsync(id);
                if (entity == null)
                {
                    _logger.LogWarning("Entity {EntityId} of type {EntityType} not found for deletion",
                        id, typeof(TEntity).Name);
                    return false;
                }

                await PreDeleteAsync(id, entity);

                EntitySet.Remove(entity);
                await _dbContext.SaveChangesAsync();

                await PostDeleteAsync(id);

                _logger.LogInformation("Successfully deleted entity {EntityId} of type {EntityType}",
                    id, typeof(TEntity).Name);

                return true;
            }
            catch (Exception ex)
            {
                HandleException(ex, "Delete", id);
                throw;
            }
        }

        public virtual async Task<bool> ExistsAsync(TId id)
        {
            try
            {
                return await EntitySet.FindAsync(id) != null;
            }
            catch (Exception ex)
            {
                HandleException(ex, "Exists", id);
                throw;
            }
        }

        public virtual async Task<(List<TResponseModel> Items, int TotalCount, int Page, int PageSize)> GetPagedAsync(int page, int pageSize)
        {
            try
            {
                _logger.LogDebug("Getting paged entities of type {EntityType}, page {Page}, size {PageSize}",
                    typeof(TEntity).Name, page, pageSize);

                var totalCount = await EntitySet.CountAsync();
                var skip = (page - 1) * pageSize;

                var entities = await EntitySet
                    .Skip(skip)
                    .Take(pageSize)
                    .ToListAsync();

                var items = entities.Select(MapToResponseModel).ToList();

                return (items, totalCount, page, pageSize);
            }
            catch (Exception ex)
            {
                HandleException(ex, "GetPaged");
                throw;
            }
        }

        public virtual async Task<(bool IsValid, List<string> Errors)> ValidateCreateAsync(TCreateModel createModel)
        {
            return await ValidateCreateInternalAsync(createModel);
        }

        public virtual async Task<(bool IsValid, List<string> Errors)> ValidateUpdateAsync(TId id, TUpdateModel updateModel)
        {
            return await ValidateUpdateInternalAsync(id, updateModel);
        }

        public virtual void HandleException(Exception ex, string operation, TId? entityId = null)
        {
            var entityIdStr = entityId?.ToString() ?? "N/A";
            _logger.LogError(ex, "Error in {Operation} operation for entity type {EntityType}, ID: {EntityId}",
                operation, typeof(TEntity).Name, entityIdStr);
        }
    }
}
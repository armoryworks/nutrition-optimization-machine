using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nom.Data;
using Nom.Data.Measurement;
using Nom.Orch.Interfaces.Measurement;
using Nom.Orch.Models.Measurement;

namespace Nom.Orch.Services.Measurement
{
    /// <summary>
    /// Service implementation for managing measurement categories.
    /// </summary>
    public class MeasurementCategoryOrchestrationService : IMeasurementCategoryOrchestrationService
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly ILogger<MeasurementCategoryOrchestrationService> _logger;

        public MeasurementCategoryOrchestrationService(
            ApplicationDbContext dbContext,
            ILogger<MeasurementCategoryOrchestrationService> logger)
        {
            _dbContext = dbContext;
            _logger = logger;
        }

        public async Task<List<MeasurementCategoryModel>> GetAllCategoriesAsync()
        {
            try
            {
                var categories = await _dbContext.MeasurementCategories
                    .Include(c => c.BaseUnit)
                    .Select(c => new MeasurementCategoryModel
                    {
                        Id = c.Id,
                        Name = c.Name,
                        Description = c.Description,
                        BaseUnitId = c.BaseUnitId,
                        BaseUnitName = c.BaseUnit.Name,
                        BaseUnitSymbol = c.BaseUnit.Symbol,
                        CreatedDate = c.CreatedDate,
                        LastModifiedDate = c.LastModifiedDate
                    })
                    .ToListAsync();

                _logger.LogInformation("Retrieved {Count} measurement categories", categories.Count);
                return categories;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving measurement categories");
                throw;
            }
        }

        public async Task<MeasurementCategoryModel?> GetCategoryByIdAsync(long id)
        {
            try
            {
                var category = await _dbContext.MeasurementCategories
                    .Include(c => c.BaseUnit)
                    .FirstOrDefaultAsync(c => c.Id == id);

                if (category == null)
                {
                    _logger.LogWarning("Measurement category with ID {Id} not found", id);
                    return null;
                }

                var model = new MeasurementCategoryModel
                {
                    Id = category.Id,
                    Name = category.Name,
                    Description = category.Description,
                    BaseUnitId = category.BaseUnitId,
                    BaseUnitName = category.BaseUnit.Name,
                    BaseUnitSymbol = category.BaseUnit.Symbol,
                    CreatedDate = category.CreatedDate,
                    LastModifiedDate = category.LastModifiedDate
                };

                _logger.LogInformation("Retrieved measurement category {Id}: {Name}", id, category.Name);
                return model;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving measurement category with ID {Id}", id);
                throw;
            }
        }

        public async Task<MeasurementCategoryModel> CreateCategoryAsync(CreateCategoryRequest request)
        {
            try
            {
                var category = new MeasurementCategoryEntity
                {
                    Name = request.Name,
                    Description = request.Description,
                    BaseUnitId = request.BaseUnitId
                };

                _dbContext.MeasurementCategories.Add(category);
                await _dbContext.SaveChangesAsync();

                var model = await GetCategoryByIdAsync(category.Id);
                _logger.LogInformation("Created measurement category {Id}: {Name}", category.Id, category.Name);
                return model!;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating measurement category {Name}", request.Name);
                throw;
            }
        }

        public async Task<bool> UpdateCategoryAsync(UpdateCategoryRequest request)
        {
            try
            {
                var category = await _dbContext.MeasurementCategories.FindAsync(request.Id);
                if (category == null)
                {
                    _logger.LogWarning("Measurement category with ID {Id} not found for update", request.Id);
                    return false;
                }

                if (request.Name != null)
                    category.Name = request.Name;
                if (request.Description != null)
                    category.Description = request.Description;
                if (request.BaseUnitId.HasValue)
                    category.BaseUnitId = request.BaseUnitId.Value;

                category.LastModifiedDate = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync();

                _logger.LogInformation("Updated measurement category {Id}: {Name}", category.Id, category.Name);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating measurement category {Id}", request.Id);
                throw;
            }
        }

        public async Task<bool> DeleteCategoryAsync(long id)
        {
            try
            {
                var category = await _dbContext.MeasurementCategories.FindAsync(id);
                if (category == null)
                {
                    _logger.LogWarning("Measurement category with ID {Id} not found for deletion", id);
                    return false;
                }

                // Check if category has measurements
                var hasMeasurements = await _dbContext.Measurements.AnyAsync(m => m.MeasurementCategoryId == id);
                if (hasMeasurements)
                {
                    _logger.LogWarning("Cannot delete measurement category {Id} - it has associated measurements", id);
                    return false;
                }

                _dbContext.MeasurementCategories.Remove(category);
                await _dbContext.SaveChangesAsync();

                _logger.LogInformation("Deleted measurement category {Id}: {Name}", id, category.Name);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting measurement category {Id}", id);
                throw;
            }
        }

        public async Task<List<MeasurementModel>> GetMeasurementsInCategoryAsync(long categoryId)
        {
            try
            {
                var measurements = await _dbContext.Measurements
                    .Include(m => m.Category)
                    .Where(m => m.MeasurementCategoryId == categoryId)
                    .Select(m => new MeasurementModel
                    {
                        Id = m.Id,
                        Name = m.Name,
                        Description = m.Description,
                        Symbol = m.Symbol,
                        CategoryId = m.MeasurementCategoryId,
                        CategoryName = m.Category.Name,
                        IsBaseUnit = m.IsBaseUnit,
                        BaseUnitConversionFactor = m.BaseUnitConversionFactor,
                        CreatedDate = m.CreatedDate,
                        LastModifiedDate = m.LastModifiedDate
                    })
                    .ToListAsync();

                _logger.LogInformation("Retrieved {Count} measurements in category {CategoryId}", measurements.Count, categoryId);
                return measurements;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving measurements in category {CategoryId}", categoryId);
                throw;
            }
        }

        public async Task<bool> SetBaseUnitAsync(long categoryId, long measurementId)
        {
            try
            {
                var category = await _dbContext.MeasurementCategories.FindAsync(categoryId);
                if (category == null)
                {
                    _logger.LogWarning("Measurement category with ID {CategoryId} not found", categoryId);
                    return false;
                }

                var measurement = await _dbContext.Measurements.FindAsync(measurementId);
                if (measurement == null)
                {
                    _logger.LogWarning("Measurement with ID {MeasurementId} not found", measurementId);
                    return false;
                }

                if (measurement.MeasurementCategoryId != categoryId)
                {
                    _logger.LogWarning("Measurement {MeasurementId} does not belong to category {CategoryId}", measurementId, categoryId);
                    return false;
                }

                // Update base unit
                category.BaseUnitId = measurementId;
                category.LastModifiedDate = DateTime.UtcNow;

                // Update measurement to be base unit
                measurement.IsBaseUnit = true;
                measurement.BaseUnitConversionFactor = 1.0m;
                measurement.LastModifiedDate = DateTime.UtcNow;

                await _dbContext.SaveChangesAsync();

                _logger.LogInformation("Set measurement {MeasurementId} as base unit for category {CategoryId}", measurementId, categoryId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting base unit {MeasurementId} for category {CategoryId}", measurementId, categoryId);
                throw;
            }
        }
    }
}

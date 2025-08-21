using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nom.Data;
using Nom.Data.Measurement;
using Nom.Orch.Interfaces.Measurement;
using Nom.Orch.Models.Measurement;

namespace Nom.Orch.Services.Measurement
{
    /// <summary>
    /// Service implementation for managing measurements and conversions.
    /// </summary>
    public class MeasurementOrchestrationService : IMeasurementOrchestrationService
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly ILogger<MeasurementOrchestrationService> _logger;

        public MeasurementOrchestrationService(
            ApplicationDbContext dbContext,
            ILogger<MeasurementOrchestrationService> logger)
        {
            _dbContext = dbContext;
            _logger = logger;
        }

        public async Task<List<MeasurementModel>> GetMeasurementsByCategoryAsync(long categoryId)
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

                _logger.LogInformation("Retrieved {Count} measurements for category {CategoryId}", measurements.Count, categoryId);
                return measurements;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving measurements for category {CategoryId}", categoryId);
                throw;
            }
        }

        public async Task<MeasurementModel?> GetMeasurementByIdAsync(long id)
        {
            try
            {
                var measurement = await _dbContext.Measurements
                    .Include(m => m.Category)
                    .FirstOrDefaultAsync(m => m.Id == id);

                if (measurement == null)
                {
                    _logger.LogWarning("Measurement with ID {Id} not found", id);
                    return null;
                }

                var model = new MeasurementModel
                {
                    Id = measurement.Id,
                    Name = measurement.Name,
                    Description = measurement.Description,
                    Symbol = measurement.Symbol,
                    CategoryId = measurement.MeasurementCategoryId,
                    CategoryName = measurement.Category.Name,
                    IsBaseUnit = measurement.IsBaseUnit,
                    BaseUnitConversionFactor = measurement.BaseUnitConversionFactor,
                    CreatedDate = measurement.CreatedDate,
                    LastModifiedDate = measurement.LastModifiedDate
                };

                _logger.LogInformation("Retrieved measurement {Id}: {Name}", id, measurement.Name);
                return model;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving measurement with ID {Id}", id);
                throw;
            }
        }

        public async Task<decimal> ConvertMeasurementAsync(long fromId, long toId, decimal value)
        {
            try
            {
                // Find direct conversion
                var conversion = await _dbContext.MeasurementConversions
                    .FirstOrDefaultAsync(c => c.FromMeasurementId == fromId && c.ToMeasurementId == toId);

                if (conversion != null)
                {
                    var result = (value * conversion.ConversionFactor) + (conversion.Offset ?? 0);
                    _logger.LogInformation("Converted {Value} from measurement {FromId} to {ToId}: {Result}", value, fromId, toId, result);
                    return result;
                }

                // TODO: Implement multi-step conversion logic
                _logger.LogWarning("Direct conversion not found from measurement {FromId} to {ToId}", fromId, toId);
                throw new InvalidOperationException($"Conversion not found from measurement {fromId} to {toId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error converting measurement {Value} from {FromId} to {ToId}", value, fromId, toId);
                throw;
            }
        }

        public async Task<List<MeasurementConversionModel>> GetConversionPathsAsync(long fromId, long toId)
        {
            try
            {
                var conversions = await _dbContext.MeasurementConversions
                    .Include(c => c.FromMeasurement)
                    .Include(c => c.ToMeasurement)
                    .Where(c => c.FromMeasurementId == fromId && c.ToMeasurementId == toId)
                    .Select(c => new MeasurementConversionModel
                    {
                        Id = c.Id,
                        FromMeasurementId = c.FromMeasurementId,
                        FromMeasurementName = c.FromMeasurement.Name,
                        FromMeasurementSymbol = c.FromMeasurement.Symbol,
                        ToMeasurementId = c.ToMeasurementId,
                        ToMeasurementName = c.ToMeasurement.Name,
                        ToMeasurementSymbol = c.ToMeasurement.Symbol,
                        ConversionFactor = c.ConversionFactor,
                        Offset = c.Offset,
                        Formula = c.Formula,
                        IsDirectConversion = c.IsDirectConversion,
                        CreatedDate = c.CreatedDate,
                        LastModifiedDate = c.LastModifiedDate
                    })
                    .ToListAsync();

                _logger.LogInformation("Retrieved {Count} conversion paths from {FromId} to {ToId}", conversions.Count, fromId, toId);
                return conversions;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving conversion paths from {FromId} to {ToId}", fromId, toId);
                throw;
            }
        }

        public async Task<List<IngredientMeasurementModel>> GetIngredientMeasurementsAsync(long ingredientId)
        {
            try
            {
                var measurements = await _dbContext.IngredientMeasurements
                    .Include(m => m.Category)
                    .Include(m => m.Ingredient)
                    .Where(m => m.IngredientId == ingredientId)
                    .Select(m => new IngredientMeasurementModel
                    {
                        Id = m.Id,
                        Name = m.Name,
                        Description = m.Description,
                        Symbol = m.Symbol,
                        CategoryId = m.MeasurementCategoryId,
                        CategoryName = m.Category.Name,
                        IsBaseUnit = m.IsBaseUnit,
                        BaseUnitConversionFactor = m.BaseUnitConversionFactor,
                        IngredientId = m.IngredientId,
                        IngredientName = m.Ingredient.Name,
                        TypicalQuantity = m.TypicalQuantity,
                        IsPreferredUnit = m.IsPreferredUnit,
                        CreatedDate = m.CreatedDate,
                        LastModifiedDate = m.LastModifiedDate
                    })
                    .ToListAsync();

                _logger.LogInformation("Retrieved {Count} measurements for ingredient {IngredientId}", measurements.Count, ingredientId);
                return measurements;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving measurements for ingredient {IngredientId}", ingredientId);
                throw;
            }
        }

        public async Task<List<NutrientMeasurementModel>> GetNutrientMeasurementsAsync(long nutrientId)
        {
            try
            {
                var measurements = await _dbContext.NutrientMeasurements
                    .Include(m => m.Category)
                    .Include(m => m.Nutrient)
                    .Where(m => m.NutrientId == nutrientId)
                    .Select(m => new NutrientMeasurementModel
                    {
                        Id = m.Id,
                        Name = m.Name,
                        Description = m.Description,
                        Symbol = m.Symbol,
                        CategoryId = m.MeasurementCategoryId,
                        CategoryName = m.Category.Name,
                        IsBaseUnit = m.IsBaseUnit,
                        BaseUnitConversionFactor = m.BaseUnitConversionFactor,
                        NutrientId = m.NutrientId,
                        NutrientName = m.Nutrient.Name,
                        StandardAmount = m.StandardAmount,
                        IsStandardUnit = m.IsStandardUnit,
                        CreatedDate = m.CreatedDate,
                        LastModifiedDate = m.LastModifiedDate
                    })
                    .ToListAsync();

                _logger.LogInformation("Retrieved {Count} measurements for nutrient {NutrientId}", measurements.Count, nutrientId);
                return measurements;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving measurements for nutrient {NutrientId}", nutrientId);
                throw;
            }
        }

        public async Task<MeasurementModel> CreateMeasurementAsync(CreateMeasurementRequest request)
        {
            try
            {
                var measurement = new BaseMeasurementEntity
                {
                    Name = request.Name,
                    Description = request.Description,
                    Symbol = request.Symbol,
                    MeasurementCategoryId = request.CategoryId,
                    IsBaseUnit = request.IsBaseUnit,
                    BaseUnitConversionFactor = request.BaseUnitConversionFactor
                };

                _dbContext.Measurements.Add(measurement);
                await _dbContext.SaveChangesAsync();

                var model = await GetMeasurementByIdAsync(measurement.Id);
                _logger.LogInformation("Created measurement {Id}: {Name}", measurement.Id, measurement.Name);
                return model!;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating measurement {Name}", request.Name);
                throw;
            }
        }

        public async Task<MeasurementConversionModel> CreateConversionAsync(CreateConversionRequest request)
        {
            try
            {
                var conversion = new MeasurementConversionEntity
                {
                    FromMeasurementId = request.FromMeasurementId,
                    ToMeasurementId = request.ToMeasurementId,
                    ConversionFactor = request.ConversionFactor,
                    Offset = request.Offset,
                    Formula = request.Formula,
                    IsDirectConversion = request.IsDirectConversion
                };

                _dbContext.MeasurementConversions.Add(conversion);
                await _dbContext.SaveChangesAsync();

                var model = await GetConversionPathsAsync(request.FromMeasurementId, request.ToMeasurementId);
                _logger.LogInformation("Created conversion {Id} from {FromId} to {ToId}", conversion.Id, request.FromMeasurementId, request.ToMeasurementId);
                return model.First();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating conversion from {FromId} to {ToId}", request.FromMeasurementId, request.ToMeasurementId);
                throw;
            }
        }

        public async Task<MeasurementModel> UpdateMeasurementAsync(long id, UpdateMeasurementRequest request)
        {
            try
            {
                var measurement = await _dbContext.Measurements.FindAsync(id);
                if (measurement == null)
                {
                    throw new InvalidOperationException($"Measurement with ID {id} not found");
                }

                if (request.Name != null)
                    measurement.Name = request.Name;
                if (request.Description != null)
                    measurement.Description = request.Description;
                if (request.Symbol != null)
                    measurement.Symbol = request.Symbol;
                if (request.CategoryId.HasValue)
                    measurement.MeasurementCategoryId = request.CategoryId.Value;
                if (request.IsBaseUnit.HasValue)
                    measurement.IsBaseUnit = request.IsBaseUnit.Value;
                if (request.BaseUnitConversionFactor.HasValue)
                    measurement.BaseUnitConversionFactor = request.BaseUnitConversionFactor.Value;

                measurement.LastModifiedDate = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync();

                var model = await GetMeasurementByIdAsync(id);
                _logger.LogInformation("Updated measurement {Id}: {Name}", id, measurement.Name);
                return model!;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating measurement {Id}", id);
                throw;
            }
        }

        public async Task<bool> DeleteMeasurementAsync(long id)
        {
            try
            {
                var measurement = await _dbContext.Measurements.FindAsync(id);
                if (measurement == null)
                {
                    _logger.LogWarning("Measurement with ID {Id} not found for deletion", id);
                    return false;
                }

                _dbContext.Measurements.Remove(measurement);
                await _dbContext.SaveChangesAsync();

                _logger.LogInformation("Deleted measurement {Id}: {Name}", id, measurement.Name);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting measurement {Id}", id);
                throw;
            }
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
    }
}

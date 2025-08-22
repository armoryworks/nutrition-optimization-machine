using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
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
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IMeasurementCacheService _cacheService;
        private readonly IMeasurementPerformanceMonitor _performanceMonitor;

        public MeasurementOrchestrationService(
            ApplicationDbContext dbContext,
            ILogger<MeasurementOrchestrationService> logger,
            IHttpContextAccessor httpContextAccessor,
            IMeasurementCacheService? cacheService = null,
            IMeasurementPerformanceMonitor? performanceMonitor = null)
        {
            _dbContext = dbContext;
            _logger = logger;
            _httpContextAccessor = httpContextAccessor;
            _cacheService = cacheService ?? new MeasurementCacheService(new MemoryCache(new MemoryCacheOptions()), 
                new Logger<MeasurementCacheService>(new LoggerFactory()));
            _performanceMonitor = performanceMonitor ?? new MeasurementPerformanceMonitor(
                new Logger<MeasurementPerformanceMonitor>(new LoggerFactory()));
        }

        public async Task<List<MeasurementModel>> GetMeasurementsByCategoryAsync(long categoryId)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                // Check cache first
                var cacheKey = $"measurements_category_{categoryId}";
                if (_cacheService.TryGetCachedMeasurementsByCategory(categoryId, out var cachedMeasurements))
                {
                    stopwatch.Stop();
                    _performanceMonitor.RecordQueryTime("GetMeasurementsByCategory", stopwatch.Elapsed);
                    _logger.LogDebug("Retrieved {Count} measurements for category {CategoryId} from cache", 
                        cachedMeasurements.Count, categoryId);
                    return cachedMeasurements;
                }

                var measurements = await _dbContext.Measurements
                    .Include(m => m.Category)
                    .Where(m => m.MeasurementCategoryId == categoryId)
                    .AsNoTracking() // Optimize for read-only operations
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

                // Cache the results
                await _cacheService.CacheMeasurementsByCategoryAsync(categoryId, measurements);

                stopwatch.Stop();
                _performanceMonitor.RecordQueryTime("GetMeasurementsByCategory", stopwatch.Elapsed);
                
                _logger.LogInformation("Retrieved {Count} measurements for category {CategoryId} in {Duration}", 
                    measurements.Count, categoryId, stopwatch.Elapsed);
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

        public async Task<List<MeasurementModel>> GetAllMeasurementsAsync()
        {
            try
            {
                // Use optimized query with single Include to reduce N+1 queries
                var measurements = await _dbContext.Measurements
                    .Include(m => m.Category)
                    .AsNoTracking() // Improve performance for read-only operations
                    .OrderBy(m => m.Category.Name)
                    .ThenBy(m => m.Name)
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

                _logger.LogInformation("Retrieved {Count} measurements", measurements.Count);
                return measurements;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving all measurements");
                throw;
            }
        }

        public async Task<Dictionary<long, List<MeasurementConversionModel>>> GetBulkConversionsAsync(List<(long FromId, long ToId)> conversionRequests)
        {
            try
            {
                var result = new Dictionary<long, List<MeasurementConversionModel>>();
                
                // Batch process conversion requests
                var fromIds = conversionRequests.Select(r => r.FromId).Distinct().ToList();
                var toIds = conversionRequests.Select(r => r.ToId).Distinct().ToList();
                
                // Single query to get all relevant conversions
                var allConversions = await _dbContext.MeasurementConversions
                    .Include(c => c.FromMeasurement)
                    .Include(c => c.ToMeasurement)
                    .Where(c => fromIds.Contains(c.FromMeasurementId) && toIds.Contains(c.ToMeasurementId))
                    .AsNoTracking()
                    .ToListAsync();

                // Process each request
                foreach (var (fromId, toId) in conversionRequests)
                {
                    var conversions = allConversions
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
                        .ToList();

                    result[fromId] = conversions;
                }

                _logger.LogInformation("Retrieved bulk conversions for {Count} requests", conversionRequests.Count);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving bulk conversions");
                throw;
            }
        }

        /// <summary>
        /// Converts a single measurement value from one unit to another with caching and performance monitoring
        /// </summary>
        public async Task<decimal> ConvertMeasurementAsync(long fromId, long toId, decimal value)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                // Check cache first
                var cachedConversion = await _cacheService.GetCachedConversionAsync(fromId, toId);
                if (cachedConversion.HasValue)
                {
                    var result = value * cachedConversion.Value;
                    stopwatch.Stop();
                    _performanceMonitor.RecordConversionTime(fromId, toId, stopwatch.Elapsed, true);
                    
                    _logger.LogInformation("Converted {Value} from measurement {FromId} to {ToId} using cache: {Result}", 
                        value, fromId, toId, result);
                    return result;
                }

                // Find direct conversion
                var conversion = await _dbContext.MeasurementConversions
                    .FirstOrDefaultAsync(c => c.FromMeasurementId == fromId && c.ToMeasurementId == toId);

                if (conversion != null)
                {
                    var result = (value * conversion.ConversionFactor) + (conversion.Offset ?? 0);
                    
                    // Cache the conversion factor
                    await _cacheService.CacheConversionAsync(fromId, toId, conversion.ConversionFactor, conversion.Offset);
                    
                    stopwatch.Stop();
                    _performanceMonitor.RecordConversionTime(fromId, toId, stopwatch.Elapsed, false);
                    
                    _logger.LogInformation("Converted {Value} from measurement {FromId} to {ToId}: {Result}", value, fromId, toId, result);
                    return result;
                }

                // Try to find conversion via base units
                var fromMeasurement = await _dbContext.Measurements.FindAsync(fromId);
                var toMeasurement = await _dbContext.Measurements.FindAsync(toId);

                if (fromMeasurement?.BaseUnitConversionFactor.HasValue == true && 
                    toMeasurement?.BaseUnitConversionFactor.HasValue == true)
                {
                    decimal result;
                    if (fromMeasurement.IsBaseUnit)
                    {
                        result = value * toMeasurement.BaseUnitConversionFactor.Value;
                    }
                    else if (toMeasurement.IsBaseUnit)
                    {
                        result = value / fromMeasurement.BaseUnitConversionFactor.Value;
                    }
                    else
                    {
                        var baseValue = value * fromMeasurement.BaseUnitConversionFactor.Value;
                        result = baseValue / toMeasurement.BaseUnitConversionFactor.Value;
                    }

                    // Cache the calculated conversion
                    var calculatedFactor = result / value;
                    await _cacheService.CacheConversionAsync(fromId, toId, calculatedFactor);
                    
                    stopwatch.Stop();
                    _performanceMonitor.RecordConversionTime(fromId, toId, stopwatch.Elapsed, false);
                    
                    _logger.LogInformation("Converted {Value} from measurement {FromId} to {ToId} via base unit: {Result}", 
                        value, fromId, toId, result);
                    return result;
                }

                // Try multi-step conversion using BFS algorithm
                var conversionPath = await FindConversionPathAsync(fromId, toId);
                if (conversionPath != null && conversionPath.Any())
                {
                    decimal result = value;
                    foreach (var step in conversionPath)
                    {
                        result = (result * step.ConversionFactor) + (step.Offset ?? 0);
                    }
                    
                    // Cache the calculated conversion factor
                    var calculatedFactor = result / value;
                    await _cacheService.CacheConversionAsync(fromId, toId, calculatedFactor);
                    
                    stopwatch.Stop();
                    _performanceMonitor.RecordConversionTime(fromId, toId, stopwatch.Elapsed, false);
                    
                    _logger.LogInformation("Converted {Value} from measurement {FromId} to {ToId} via {StepCount} steps: {Result}", 
                        value, fromId, toId, conversionPath.Count, result);
                    return result;
                }

                _logger.LogWarning("No conversion path found from measurement {FromId} to {ToId}", fromId, toId);
                throw new InvalidOperationException($"Conversion not found from measurement {fromId} to {toId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error converting measurement {Value} from {FromId} to {ToId}", value, fromId, toId);
                throw;
            }
            finally
            {
                stopwatch.Stop();
                // Performance recording is now handled within each conversion path
                // to avoid double-counting and incorrect cache hit/miss tracking
            }
        }

        /// <summary>
        /// Bulk converts multiple measurement values efficiently with optimized database queries and caching
        /// </summary>
        public async Task<List<decimal>> BulkConvertMeasurementsAsync(List<(long fromId, long toId, decimal value)> conversions)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                if (conversions == null || !conversions.Any())
                {
                    return new List<decimal>();
                }

                var results = new List<decimal>();
                var cacheMisses = new List<(long fromId, long toId, decimal value)>();
                var cacheHits = 0;

                // Check cache for all conversions first
                foreach (var (fromId, toId, value) in conversions)
                {
                    var cachedConversion = await _cacheService.GetCachedConversionAsync(fromId, toId);
                    if (cachedConversion.HasValue)
                    {
                        results.Add(value * cachedConversion.Value);
                        cacheHits++;
                    }
                    else
                    {
                        cacheMisses.Add((fromId, toId, value));
                        results.Add(0); // Placeholder, will be filled later
                    }
                }

                _logger.LogDebug("Bulk conversion: {CacheHits} cache hits, {CacheMisses} cache misses out of {Total} conversions", 
                    cacheHits, cacheMisses.Count, conversions.Count);

                // If all conversions were cached, return immediately
                if (!cacheMisses.Any())
                {
                    stopwatch.Stop();
                    _performanceMonitor.RecordConversionTime(0, 0, stopwatch.Elapsed, true);
                    return results;
                }

                // Process cache misses efficiently
                var uniqueFromIds = cacheMisses.Select(c => c.fromId).Distinct().ToList();
                var uniqueToIds = cacheMisses.Select(c => c.toId).Distinct().ToList();

                // Single query to get all relevant conversions
                var allConversions = await _dbContext.MeasurementConversions
                    .AsNoTracking()
                    .Where(c => uniqueFromIds.Contains(c.FromMeasurementId) && uniqueToIds.Contains(c.ToMeasurementId))
                    .ToListAsync();

                // Single query to get all relevant measurements for base unit conversions
                var allMeasurements = await _dbContext.Measurements
                    .AsNoTracking()
                    .Where(m => uniqueFromIds.Contains(m.Id) || uniqueToIds.Contains(m.Id))
                    .Cast<MeasurementEntity>()
                    .ToListAsync();

                // Process each cache miss
                for (int i = 0; i < cacheMisses.Count; i++)
                {
                    var (fromId, toId, value) = cacheMisses[i];
                    var result = await ProcessSingleConversionAsync(fromId, toId, value, allConversions, allMeasurements);
                    
                    // Find the index in the original results list and update it
                    var originalIndex = conversions.IndexOf((fromId, toId, value));
                    if (originalIndex >= 0)
                    {
                        results[originalIndex] = result;
                    }
                }

                stopwatch.Stop();
                _performanceMonitor.RecordConversionTime(0, 0, stopwatch.Elapsed, false);
                
                _logger.LogInformation("Bulk converted {Count} measurements in {Duration} ({CacheHits} from cache)", 
                    conversions.Count, stopwatch.Elapsed, cacheHits);
                
                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in bulk conversion of {Count} measurements", conversions.Count);
                throw;
            }
        }

        /// <summary>
        /// Helper method to process a single conversion using pre-loaded data
        /// </summary>
        private async Task<decimal> ProcessSingleConversionAsync(long fromId, long toId, decimal value, 
            List<MeasurementConversionEntity> allConversions, List<MeasurementEntity> allMeasurements)
        {
            // Check direct conversion
            var conversion = allConversions.FirstOrDefault(c => c.FromMeasurementId == fromId && c.ToMeasurementId == toId);
            if (conversion != null)
            {
                var result = (value * conversion.ConversionFactor) + (conversion.Offset ?? 0);
                await _cacheService.CacheConversionAsync(fromId, toId, conversion.ConversionFactor, conversion.Offset);
                return result;
            }

            // Try base unit conversion
            var fromMeasurement = allMeasurements.FirstOrDefault(m => m.Id == fromId);
            var toMeasurement = allMeasurements.FirstOrDefault(m => m.Id == toId);

            if (fromMeasurement?.BaseUnitConversionFactor.HasValue == true && 
                toMeasurement?.BaseUnitConversionFactor.HasValue == true)
            {
                decimal result;
                if (fromMeasurement.IsBaseUnit)
                {
                    result = value * toMeasurement.BaseUnitConversionFactor.Value;
                }
                else if (toMeasurement.IsBaseUnit)
                {
                    result = value / fromMeasurement.BaseUnitConversionFactor.Value;
                }
                else
                {
                    var baseValue = value * fromMeasurement.BaseUnitConversionFactor.Value;
                    result = baseValue / toMeasurement.BaseUnitConversionFactor.Value;
                }

                var calculatedFactor = result / value;
                await _cacheService.CacheConversionAsync(fromId, toId, calculatedFactor);
                return result;
            }

            // Try multi-step conversion
            var conversionPath = await FindConversionPathAsync(fromId, toId);
            if (conversionPath != null && conversionPath.Any())
            {
                decimal result = value;
                foreach (var step in conversionPath)
                {
                    result = (result * step.ConversionFactor) + (step.Offset ?? 0);
                }
                
                var calculatedFactor = result / value;
                await _cacheService.CacheConversionAsync(fromId, toId, calculatedFactor);
                return result;
            }

            throw new InvalidOperationException($"Conversion not found from measurement {fromId} to {toId}");
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
                    .Include(m => m.Ingredient)
                    .Where(m => m.IngredientId == ingredientId)
                    .Select(m => new IngredientMeasurementModel
                    {
                        Id = m.Id,
                        IngredientId = m.IngredientId,
                        IngredientName = m.Ingredient.Name,
                        PreferredMeasurementId = m.Id,
                        PreferredMeasurementName = m.Name,
                        PreferredMeasurementSymbol = m.Symbol,
                        IsPreferred = m.IsPreferredUnit,
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
                    .Include(m => m.Nutrient)
                    .Where(m => m.NutrientId == nutrientId)
                    .Select(m => new NutrientMeasurementModel
                    {
                        Id = m.Id,
                        NutrientId = m.NutrientId,
                        NutrientName = m.Nutrient.Name,
                        StandardMeasurementId = m.Id,
                        StandardMeasurementName = m.Name,
                        StandardMeasurementSymbol = m.Symbol,
                        StandardDailyValue = m.StandardAmount,
                        StandardDailyValueUnit = m.Symbol,
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

        public async Task<IngredientMeasurementModel> CreateIngredientMeasurementAsync(CreateIngredientMeasurementRequest request)
        {
            try
            {
                // Get the measurement details from the preferred measurement ID
                var measurement = await _dbContext.Measurements.FindAsync(request.PreferredMeasurementId);
                if (measurement == null)
                {
                    throw new InvalidOperationException($"Measurement with ID {request.PreferredMeasurementId} not found");
                }

                var ingredientMeasurement = new IngredientMeasurementEntity
                {
                    IngredientId = request.IngredientId,
                    Name = measurement.Name,
                    Symbol = measurement.Symbol,
                    IsPreferredUnit = request.IsPreferred,
                    CreatedDate = DateTime.UtcNow,
                    CreatedByPersonId = 1 // TODO: Get from current user context
                };

                _dbContext.IngredientMeasurements.Add(ingredientMeasurement);
                await _dbContext.SaveChangesAsync();

                var model = new IngredientMeasurementModel
                {
                    Id = ingredientMeasurement.Id,
                    IngredientId = ingredientMeasurement.IngredientId,
                    PreferredMeasurementId = ingredientMeasurement.Id,
                    PreferredMeasurementName = ingredientMeasurement.Name,
                    PreferredMeasurementSymbol = ingredientMeasurement.Symbol,
                    IsPreferred = ingredientMeasurement.IsPreferredUnit,
                    CreatedDate = ingredientMeasurement.CreatedDate,
                    LastModifiedDate = ingredientMeasurement.LastModifiedDate
                };

                _logger.LogInformation("Created ingredient measurement {Id} for ingredient {IngredientId}", 
                    ingredientMeasurement.Id, request.IngredientId);
                return model;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating ingredient measurement for ingredient {IngredientId}", request.IngredientId);
                throw;
            }
        }

        public async Task<IngredientMeasurementModel?> UpdateIngredientMeasurementAsync(long id, UpdateIngredientMeasurementRequest request)
        {
            try
            {
                var ingredientMeasurement = await _dbContext.IngredientMeasurements.FindAsync(id);
                if (ingredientMeasurement == null)
                {
                    _logger.LogWarning("Ingredient measurement with ID {Id} not found for update", id);
                    return null;
                }

                // Update the preferred measurement if changed
                if (request.PreferredMeasurementId != ingredientMeasurement.Id)
                {
                    var newMeasurement = await _dbContext.Measurements.FindAsync(request.PreferredMeasurementId);
                    if (newMeasurement != null)
                    {
                        ingredientMeasurement.Name = newMeasurement.Name;
                        ingredientMeasurement.Symbol = newMeasurement.Symbol;
                    }
                }

                ingredientMeasurement.IsPreferredUnit = request.IsPreferred;
                ingredientMeasurement.LastModifiedDate = DateTime.UtcNow;

                await _dbContext.SaveChangesAsync();

                var model = new IngredientMeasurementModel
                {
                    Id = ingredientMeasurement.Id,
                    IngredientId = ingredientMeasurement.IngredientId,
                    PreferredMeasurementId = ingredientMeasurement.Id,
                    PreferredMeasurementName = ingredientMeasurement.Name,
                    PreferredMeasurementSymbol = ingredientMeasurement.Symbol,
                    IsPreferred = ingredientMeasurement.IsPreferredUnit,
                    CreatedDate = ingredientMeasurement.CreatedDate,
                    LastModifiedDate = ingredientMeasurement.LastModifiedDate
                };

                _logger.LogInformation("Updated ingredient measurement {Id}", id);
                return model;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating ingredient measurement {Id}", id);
                throw;
            }
        }

        public async Task<bool> DeleteIngredientMeasurementAsync(long id)
        {
            try
            {
                var ingredientMeasurement = await _dbContext.IngredientMeasurements.FindAsync(id);
                if (ingredientMeasurement == null)
                {
                    _logger.LogWarning("Ingredient measurement with ID {Id} not found for deletion", id);
                    return false;
                }

                _dbContext.IngredientMeasurements.Remove(ingredientMeasurement);
                await _dbContext.SaveChangesAsync();

                _logger.LogInformation("Deleted ingredient measurement {Id}", id);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting ingredient measurement {Id}", id);
                throw;
            }
        }

        public async Task<NutrientMeasurementModel> CreateNutrientMeasurementAsync(CreateNutrientMeasurementRequest request)
        {
            try
            {
                // Get the measurement details from the standard measurement ID
                var measurement = await _dbContext.Measurements.FindAsync(request.StandardMeasurementId);
                if (measurement == null)
                {
                    throw new InvalidOperationException($"Measurement with ID {request.StandardMeasurementId} not found");
                }

                var nutrientMeasurement = new NutrientMeasurementEntity
                {
                    NutrientId = request.NutrientId,
                    Name = measurement.Name,
                    Symbol = measurement.Symbol,
                    StandardAmount = request.StandardDailyValue ?? 0,
                    CreatedDate = DateTime.UtcNow,
                    CreatedByPersonId = 1 // TODO: Get from current user context
                };

                _dbContext.NutrientMeasurements.Add(nutrientMeasurement);
                await _dbContext.SaveChangesAsync();

                var model = new NutrientMeasurementModel
                {
                    Id = nutrientMeasurement.Id,
                    NutrientId = nutrientMeasurement.NutrientId,
                    StandardMeasurementId = nutrientMeasurement.Id,
                    StandardMeasurementName = nutrientMeasurement.Name,
                    StandardMeasurementSymbol = nutrientMeasurement.Symbol,
                    StandardDailyValue = nutrientMeasurement.StandardAmount,
                    StandardDailyValueUnit = nutrientMeasurement.Symbol,
                    CreatedDate = nutrientMeasurement.CreatedDate,
                    LastModifiedDate = nutrientMeasurement.LastModifiedDate
                };

                _logger.LogInformation("Created nutrient measurement {Id} for nutrient {NutrientId}", 
                    nutrientMeasurement.Id, request.NutrientId);
                return model;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating nutrient measurement for nutrient {NutrientId}", request.NutrientId);
                throw;
            }
        }

        public async Task<NutrientMeasurementModel?> UpdateNutrientMeasurementAsync(long id, UpdateNutrientMeasurementRequest request)
        {
            try
            {
                var nutrientMeasurement = await _dbContext.NutrientMeasurements.FindAsync(id);
                if (nutrientMeasurement == null)
                {
                    _logger.LogWarning("Nutrient measurement with ID {Id} not found for update", id);
                    return null;
                }

                // Update the standard measurement if changed
                if (request.StandardMeasurementId != nutrientMeasurement.Id)
                {
                    var newMeasurement = await _dbContext.Measurements.FindAsync(request.StandardMeasurementId);
                    if (newMeasurement != null)
                    {
                        nutrientMeasurement.Name = newMeasurement.Name;
                        nutrientMeasurement.Symbol = newMeasurement.Symbol;
                    }
                }

                nutrientMeasurement.StandardAmount = request.StandardDailyValue ?? nutrientMeasurement.StandardAmount;
                nutrientMeasurement.LastModifiedDate = DateTime.UtcNow;

                await _dbContext.SaveChangesAsync();

                var model = new NutrientMeasurementModel
                {
                    Id = nutrientMeasurement.Id,
                    NutrientId = nutrientMeasurement.NutrientId,
                    StandardMeasurementId = nutrientMeasurement.Id,
                    StandardMeasurementName = nutrientMeasurement.Name,
                    StandardMeasurementSymbol = nutrientMeasurement.Symbol,
                    StandardDailyValue = nutrientMeasurement.StandardAmount,
                    StandardDailyValueUnit = nutrientMeasurement.Symbol,
                    CreatedDate = nutrientMeasurement.CreatedDate,
                    LastModifiedDate = nutrientMeasurement.LastModifiedDate
                };

                _logger.LogInformation("Updated nutrient measurement {Id}", id);
                return model;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating nutrient measurement {Id}", id);
                throw;
            }
        }

        public async Task<bool> DeleteNutrientMeasurementAsync(long id)
        {
            try
            {
                var nutrientMeasurement = await _dbContext.NutrientMeasurements.FindAsync(id);
                if (nutrientMeasurement == null)
                {
                    _logger.LogWarning("Nutrient measurement with ID {Id} not found for deletion", id);
                    return false;
                }

                _dbContext.NutrientMeasurements.Remove(nutrientMeasurement);
                await _dbContext.SaveChangesAsync();

                _logger.LogInformation("Deleted nutrient measurement {Id}", id);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting nutrient measurement {Id}", id);
                throw;
            }
        }

        /// <summary>
        /// Finds the shortest conversion path between two measurements using BFS algorithm.
        /// This is optimized for performance with caching and early termination.
        /// </summary>
        private async Task<List<MeasurementConversionEntity>?> FindConversionPathAsync(long fromId, long toId)
        {
            try
            {
                // Check cache for conversion path entities
                var cachedPath = await _cacheService.GetCachedConversionPathEntitiesAsync(fromId, toId);
                if (cachedPath != null)
                {
                    _logger.LogDebug("Found cached conversion path from {FromId} to {ToId}", fromId, toId);
                    return cachedPath;
                }

                // Load all conversions in a single query for BFS traversal
                var allConversions = await _dbContext.MeasurementConversions
                    .AsNoTracking()
                    .ToListAsync();

                // Build adjacency list for efficient graph traversal
                var graph = new Dictionary<long, List<MeasurementConversionEntity>>();
                foreach (var conversion in allConversions)
                {
                    if (!graph.ContainsKey(conversion.FromMeasurementId))
                        graph[conversion.FromMeasurementId] = new List<MeasurementConversionEntity>();
                    graph[conversion.FromMeasurementId].Add(conversion);
                }

                // BFS with path tracking
                var queue = new Queue<List<MeasurementConversionEntity>>();
                var visited = new HashSet<long>();
                
                // Start with empty path from source
                queue.Enqueue(new List<MeasurementConversionEntity>());
                visited.Add(fromId);

                while (queue.Count > 0)
                {
                    var currentPath = queue.Dequeue();
                    var currentId = currentPath.Count == 0 ? fromId : currentPath.Last().ToMeasurementId;

                    // Check if we've reached the target
                    if (currentId == toId)
                    {
                        // Cache the found path
                        await _cacheService.CacheConversionPathEntitiesAsync(fromId, toId, currentPath);
                        return currentPath;
                    }

                    // Explore neighbors
                    if (graph.ContainsKey(currentId))
                    {
                        foreach (var conversion in graph[currentId])
                        {
                            if (!visited.Contains(conversion.ToMeasurementId))
                            {
                                visited.Add(conversion.ToMeasurementId);
                                var newPath = new List<MeasurementConversionEntity>(currentPath) { conversion };
                                queue.Enqueue(newPath);
                            }
                        }
                    }
                }

                // No path found
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error finding conversion path from {FromId} to {ToId}", fromId, toId);
                return null;
            }
        }
    }
}

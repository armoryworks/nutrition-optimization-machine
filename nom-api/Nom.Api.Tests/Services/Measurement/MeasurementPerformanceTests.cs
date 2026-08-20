using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using AutoFixture;
using AutoFixture.AutoMoq;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;
using Nom.Data;
using Nom.Data.Measurement;
using Nom.Orch.Models.Measurement;
using Nom.Orch.Services.Measurement;
using Xunit;

namespace Nom.Api.Tests.Services.Measurement
{
    public class MeasurementPerformanceTests
    {
        private readonly DbContextOptions<ApplicationDbContext> _dbContextOptions;
        private readonly IFixture _fixture;
        private readonly Mock<ILogger<MeasurementOrchestrationService>> _loggerMock;
        private readonly Mock<ILogger<MeasurementCacheService>> _cacheLoggerMock;
        private readonly Mock<ILogger<MeasurementPerformanceMonitor>> _perfLoggerMock;
        private readonly Mock<IHttpContextAccessor> _httpContextAccessorMock;
        private readonly IMemoryCache _memoryCache;

        public MeasurementPerformanceTests()
        {
            _dbContextOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            _fixture = new Fixture().Customize(new AutoMoqCustomization());
            _loggerMock = new Mock<ILogger<MeasurementOrchestrationService>>();
            _cacheLoggerMock = new Mock<ILogger<MeasurementCacheService>>();
            _perfLoggerMock = new Mock<ILogger<MeasurementPerformanceMonitor>>();
            _httpContextAccessorMock = new Mock<IHttpContextAccessor>();
            _memoryCache = new MemoryCache(new MemoryCacheOptions());
        }

        [Fact]
        public async Task ConvertMeasurementAsync_ShouldCompleteWithinReasonableTime()
        {
            // Arrange
            using var context = new ApplicationDbContext(_dbContextOptions);
            var cacheService = new MeasurementCacheService(_memoryCache, _cacheLoggerMock.Object);
            var performanceMonitor = new MeasurementPerformanceMonitor(_perfLoggerMock.Object);
            var service = new MeasurementOrchestrationService(context, _loggerMock.Object, _httpContextAccessorMock.Object, cacheService, performanceMonitor);

            // Seed test data
            await SeedTestDataAsync(context);

            // Act
            var stopwatch = Stopwatch.StartNew();
            var result = await service.ConvertMeasurementAsync(1, 2, 100.0m);
            stopwatch.Stop();

            // Assert
            result.Should().BeGreaterThan(0);
            stopwatch.ElapsedMilliseconds.Should().BeLessThan(2000); // generous: shared CI runners jitter wall-clock
        }

        [Fact]
        public async Task ConvertMeasurementAsync_ShouldUseCacheForSubsequentCalls()
        {
            // Arrange
            using var context = new ApplicationDbContext(_dbContextOptions);
            var cacheService = new MeasurementCacheService(_memoryCache, _cacheLoggerMock.Object);
            var performanceMonitor = new MeasurementPerformanceMonitor(_perfLoggerMock.Object);
            var service = new MeasurementOrchestrationService(context, _loggerMock.Object, _httpContextAccessorMock.Object, cacheService, performanceMonitor);

            // Seed test data
            await SeedTestDataAsync(context);

            // Act - First call (should hit database)
            var firstCallStopwatch = Stopwatch.StartNew();
            var firstResult = await service.ConvertMeasurementAsync(1, 2, 100.0m);
            firstCallStopwatch.Stop();

            // Second call (should hit cache)
            var secondCallStopwatch = Stopwatch.StartNew();
            var secondResult = await service.ConvertMeasurementAsync(1, 2, 100.0m);
            secondCallStopwatch.Stop();

            // Assert
            firstResult.Should().Be(secondResult);
            secondCallStopwatch.ElapsedMilliseconds.Should().BeLessThanOrEqualTo(firstCallStopwatch.ElapsedMilliseconds + 50); // cached call may tie at 0ms; jitter tolerance
        }

        [Fact]
        public async Task GetBulkConversionsAsync_ShouldHandleMultipleRequestsEfficiently()
        {
            // Arrange
            using var context = new ApplicationDbContext(_dbContextOptions);
            var cacheService = new MeasurementCacheService(_memoryCache, _cacheLoggerMock.Object);
            var performanceMonitor = new MeasurementPerformanceMonitor(_perfLoggerMock.Object);
            var service = new MeasurementOrchestrationService(context, _loggerMock.Object, _httpContextAccessorMock.Object, cacheService, performanceMonitor);

            // Seed test data
            await SeedTestDataAsync(context);

            var conversionRequests = new List<(long FromId, long ToId)>
            {
                (1, 2),
                (2, 3),
                (3, 4),
                (4, 5)
            };

            // Act
            var stopwatch = Stopwatch.StartNew();
            var results = await service.GetBulkConversionsAsync(conversionRequests);
            stopwatch.Stop();

            // Assert
            results.Should().HaveCount(4);
            stopwatch.ElapsedMilliseconds.Should().BeLessThan(2000); // generous: shared CI runners jitter wall-clock
        }

        [Fact]
        public async Task PerformanceMonitor_ShouldTrackMetricsCorrectly()
        {
            // Arrange
            using var context = new ApplicationDbContext(_dbContextOptions);
            var cacheService = new MeasurementCacheService(_memoryCache, _cacheLoggerMock.Object);
            var performanceMonitor = new MeasurementPerformanceMonitor(_perfLoggerMock.Object);
            var service = new MeasurementOrchestrationService(context, _loggerMock.Object, _httpContextAccessorMock.Object, cacheService, performanceMonitor);

            // Seed test data
            await SeedTestDataAsync(context);

            // Get the actual IDs from seeded data
            var gram = context.Measurements.First(m => m.Name == "Gram");
            var kilogram = context.Measurements.First(m => m.Name == "Kilogram");
            
            // Act
            var result1 = await service.ConvertMeasurementAsync(gram.Id, kilogram.Id, 100.0m);
            var result2 = await service.ConvertMeasurementAsync(gram.Id, kilogram.Id, 200.0m); // Should use cache
            
            // Debug output
            Console.WriteLine($"First conversion result: {result1}");
            Console.WriteLine($"Second conversion result: {result2}");
            Console.WriteLine($"Gram ID: {gram.Id}, Kilogram ID: {kilogram.Id}");

                        var stats = performanceMonitor.GetPerformanceStats();
            
            // Debug output
            Console.WriteLine($"Total conversions: {stats.TotalConversions}");
            Console.WriteLine($"Cache hits: {stats.CacheHits}");
            Console.WriteLine($"Cache misses: {stats.CacheMisses}");
            Console.WriteLine($"Cache hit rate: {stats.CacheHitRate}%");
            Console.WriteLine($"Average conversion time: {stats.AverageConversionTime}");
            
            // Assert
            stats.TotalConversions.Should().Be(2);
            stats.CacheHits.Should().Be(1);
            stats.CacheMisses.Should().Be(1);
            stats.CacheHitRate.Should().Be(50.0);
            stats.AverageConversionTime.Should().BeGreaterThan(TimeSpan.Zero);
        }

        private async Task SeedTestDataAsync(ApplicationDbContext context)
        {
            // Create measurement categories
            var massCategory = new MeasurementCategoryEntity
            {
                Name = "Mass",
                Description = "Mass measurements",
                CreatedDate = DateTime.UtcNow,
                CreatedByPersonId = 1
            };

            var volumeCategory = new MeasurementCategoryEntity
            {
                Name = "Volume",
                Description = "Volume measurements",
                CreatedDate = DateTime.UtcNow,
                CreatedByPersonId = 1
            };

            context.MeasurementCategories.AddRange(massCategory, volumeCategory);
            await context.SaveChangesAsync();

            // Create base measurements
            var gram = new BaseMeasurementEntity
            {
                Name = "Gram",
                Symbol = "g",
                MeasurementCategoryId = massCategory.Id,
                IsBaseUnit = true,
                BaseUnitConversionFactor = 1.0m,
                CreatedDate = DateTime.UtcNow,
                CreatedByPersonId = 1
            };

            var kilogram = new BaseMeasurementEntity
            {
                Name = "Kilogram",
                Symbol = "kg",
                MeasurementCategoryId = massCategory.Id,
                IsBaseUnit = false,
                BaseUnitConversionFactor = 1000.0m,
                CreatedDate = DateTime.UtcNow,
                CreatedByPersonId = 1
            };

            var milliliter = new BaseMeasurementEntity
            {
                Name = "Milliliter",
                Symbol = "ml",
                MeasurementCategoryId = volumeCategory.Id,
                IsBaseUnit = false,
                BaseUnitConversionFactor = 1.0m,
                CreatedDate = DateTime.UtcNow,
                CreatedByPersonId = 1
            };

            var liter = new BaseMeasurementEntity
            {
                Name = "Liter",
                Symbol = "L",
                MeasurementCategoryId = volumeCategory.Id,
                IsBaseUnit = true,
                BaseUnitConversionFactor = 1000.0m,
                CreatedDate = DateTime.UtcNow,
                CreatedByPersonId = 1
            };

            var cup = new BaseMeasurementEntity
            {
                Name = "Cup",
                Symbol = "cup",
                MeasurementCategoryId = volumeCategory.Id,
                IsBaseUnit = false,
                BaseUnitConversionFactor = 236.588m,
                CreatedDate = DateTime.UtcNow,
                CreatedByPersonId = 1
            };

            context.Measurements.AddRange(gram, kilogram, milliliter, liter, cup);
            await context.SaveChangesAsync();

            // Update categories with base units
            massCategory.BaseUnitId = gram.Id;
            volumeCategory.BaseUnitId = liter.Id;
            await context.SaveChangesAsync();

            // Create conversion rules
            var conversions = new List<MeasurementConversionEntity>
            {
                new MeasurementConversionEntity
                {
                    FromMeasurementId = gram.Id,
                    ToMeasurementId = kilogram.Id,
                    ConversionFactor = 0.001m,
                    IsDirectConversion = true,
                    CreatedDate = DateTime.UtcNow,
                    CreatedByPersonId = 1
                },
                new MeasurementConversionEntity
                {
                    FromMeasurementId = milliliter.Id,
                    ToMeasurementId = liter.Id,
                    ConversionFactor = 0.001m,
                    IsDirectConversion = true,
                    CreatedDate = DateTime.UtcNow,
                    CreatedByPersonId = 1
                },
                new MeasurementConversionEntity
                {
                    FromMeasurementId = cup.Id,
                    ToMeasurementId = milliliter.Id,
                    ConversionFactor = 236.588m,
                    IsDirectConversion = true,
                    CreatedDate = DateTime.UtcNow,
                    CreatedByPersonId = 1
                }
            };

            context.MeasurementConversions.AddRange(conversions);
            await context.SaveChangesAsync();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AutoFixture;
using AutoFixture.AutoMoq;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Nom.Data;
using Nom.Data.Measurement;
using Nom.Orch.Models.Measurement;
using Nom.Orch.Services.Measurement;
using Xunit;

namespace Nom.Api.Tests.Services.Measurement
{
    public class MeasurementOrchestrationServiceTests
    {
        private readonly Fixture _fixture;
        private readonly Mock<ILogger<MeasurementOrchestrationService>> _loggerMock;
        private readonly Mock<IHttpContextAccessor> _httpContextAccessorMock;
        private readonly DbContextOptions<ApplicationDbContext> _dbContextOptions;

        public MeasurementOrchestrationServiceTests()
        {
            _fixture = new Fixture();
            _fixture.Behaviors.OfType<ThrowingRecursionBehavior>().ToList()
                .ForEach(b => _fixture.Behaviors.Remove(b));
            _fixture.Behaviors.Add(new OmitOnRecursionBehavior());

            _loggerMock = new Mock<ILogger<MeasurementOrchestrationService>>();
            _httpContextAccessorMock = new Mock<IHttpContextAccessor>();
            
            _dbContextOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;
        }

        [Fact]
        public async Task GetMeasurementsByCategoryAsync_ShouldReturnMeasurements_WhenCategoryExists()
        {
            // Arrange
            using var context = new ApplicationDbContext(_dbContextOptions);
            var service = new MeasurementOrchestrationService(context, _loggerMock.Object, _httpContextAccessorMock.Object);

            var category = new MeasurementCategoryEntity
            {
                Name = "Mass",
                Description = "Mass measurements",
                CreatedDate = DateTime.UtcNow,
                CreatedByPersonId = 1
            };
            context.MeasurementCategories.Add(category);
            await context.SaveChangesAsync();

            var measurement = new BaseMeasurementEntity
            {
                Name = "Gram",
                Symbol = "g",
                MeasurementCategoryId = category.Id,
                IsBaseUnit = true,
                BaseUnitConversionFactor = 1.0m,
                CreatedDate = DateTime.UtcNow,
                CreatedByPersonId = 1
            };
            context.Measurements.Add(measurement);
            await context.SaveChangesAsync();

            // Act
            var result = await service.GetMeasurementsByCategoryAsync(category.Id);

            // Assert
            result.Should().NotBeNull();
            result.Should().HaveCount(1);
            result[0].Name.Should().Be("Gram");
            result[0].Symbol.Should().Be("g");
            result[0].CategoryId.Should().Be(category.Id);
        }

        [Fact]
        public async Task GetMeasurementByIdAsync_ShouldReturnMeasurement_WhenMeasurementExists()
        {
            // Arrange
            using var context = new ApplicationDbContext(_dbContextOptions);
            var service = new MeasurementOrchestrationService(context, _loggerMock.Object, _httpContextAccessorMock.Object);

            var category = new MeasurementCategoryEntity
            {
                Name = "Volume",
                Description = "Volume measurements",
                CreatedDate = DateTime.UtcNow,
                CreatedByPersonId = 1
            };
            context.MeasurementCategories.Add(category);
            await context.SaveChangesAsync();

            var measurement = new BaseMeasurementEntity
            {
                Name = "Liter",
                Symbol = "L",
                MeasurementCategoryId = category.Id,
                IsBaseUnit = true,
                BaseUnitConversionFactor = 1.0m,
                CreatedDate = DateTime.UtcNow,
                CreatedByPersonId = 1
            };
            context.Measurements.Add(measurement);
            await context.SaveChangesAsync();

            // Act
            var result = await service.GetMeasurementByIdAsync(measurement.Id);

            // Assert
            result.Should().NotBeNull();
            result!.Name.Should().Be("Liter");
            result.Symbol.Should().Be("L");
            result.CategoryId.Should().Be(category.Id);
        }

        [Fact]
        public async Task GetMeasurementByIdAsync_ShouldReturnNull_WhenMeasurementDoesNotExist()
        {
            // Arrange
            using var context = new ApplicationDbContext(_dbContextOptions);
            var service = new MeasurementOrchestrationService(context, _loggerMock.Object, _httpContextAccessorMock.Object);

            // Act
            var result = await service.GetMeasurementByIdAsync(999);

            // Assert
            result.Should().BeNull();
        }

        [Fact]
        public async Task ConvertMeasurementAsync_ShouldConvertValue_WhenDirectConversionExists()
        {
            // Arrange
            using var context = new ApplicationDbContext(_dbContextOptions);
            var service = new MeasurementOrchestrationService(context, _loggerMock.Object, _httpContextAccessorMock.Object);

            var category = new MeasurementCategoryEntity
            {
                Name = "Mass",
                Description = "Mass measurements",
                CreatedDate = DateTime.UtcNow,
                CreatedByPersonId = 1
            };
            context.MeasurementCategories.Add(category);
            await context.SaveChangesAsync();

            var gram = new BaseMeasurementEntity
            {
                Name = "Gram",
                Symbol = "g",
                MeasurementCategoryId = category.Id,
                IsBaseUnit = true,
                BaseUnitConversionFactor = 1.0m,
                CreatedDate = DateTime.UtcNow,
                CreatedByPersonId = 1
            };
            context.Measurements.Add(gram);

            var kilogram = new BaseMeasurementEntity
            {
                Name = "Kilogram",
                Symbol = "kg",
                MeasurementCategoryId = category.Id,
                IsBaseUnit = false,
                BaseUnitConversionFactor = 1000.0m,
                CreatedDate = DateTime.UtcNow,
                CreatedByPersonId = 1
            };
            context.Measurements.Add(kilogram);
            await context.SaveChangesAsync();

            var conversion = new MeasurementConversionEntity
            {
                FromMeasurementId = gram.Id,
                ToMeasurementId = kilogram.Id,
                ConversionFactor = 0.001m,
                IsDirectConversion = true,
                CreatedDate = DateTime.UtcNow,
                CreatedByPersonId = 1
            };
            context.MeasurementConversions.Add(conversion);
            await context.SaveChangesAsync();

            // Act
            var result = await service.ConvertMeasurementAsync(gram.Id, kilogram.Id, 1000);

            // Assert
            result.Should().Be(1.0m);
        }

        [Fact]
        public async Task GetAllCategoriesAsync_ShouldReturnAllCategories()
        {
            // Arrange
            using var context = new ApplicationDbContext(_dbContextOptions);
            var service = new MeasurementOrchestrationService(context, _loggerMock.Object, _httpContextAccessorMock.Object);

            var categories = new List<MeasurementCategoryEntity>
            {
                new MeasurementCategoryEntity
                {
                    Name = "Mass",
                    Description = "Mass measurements",
                    CreatedDate = DateTime.UtcNow,
                    CreatedByPersonId = 1
                },
                new MeasurementCategoryEntity
                {
                    Name = "Volume",
                    Description = "Volume measurements",
                    CreatedDate = DateTime.UtcNow,
                    CreatedByPersonId = 1
                }
            };
            context.MeasurementCategories.AddRange(categories);
            await context.SaveChangesAsync();

            // Act
            var result = await service.GetAllCategoriesAsync();

            // Assert
            result.Should().NotBeNull();
            result.Should().HaveCount(2);
            result.Should().Contain(c => c.Name == "Mass");
            result.Should().Contain(c => c.Name == "Volume");
        }

        [Fact]
        public async Task GetCategoryByIdAsync_ShouldReturnCategory_WhenCategoryExists()
        {
            // Arrange
            using var context = new ApplicationDbContext(_dbContextOptions);
            var service = new MeasurementOrchestrationService(context, _loggerMock.Object, _httpContextAccessorMock.Object);

            var baseUnit = new BaseMeasurementEntity
            {
                Name = "Celsius",
                Symbol = "°C",
                MeasurementCategoryId = 0, // Will be updated after category creation
                IsBaseUnit = true,
                BaseUnitConversionFactor = 1.0m,
                CreatedDate = DateTime.UtcNow,
                CreatedByPersonId = 1
            };

            var category = new MeasurementCategoryEntity
            {
                Name = "Temperature",
                Description = "Temperature measurements",
                CreatedDate = DateTime.UtcNow,
                CreatedByPersonId = 1
            };
            context.MeasurementCategories.Add(category);
            await context.SaveChangesAsync();

            // Update base unit with correct category ID and set as base unit for category
            baseUnit.MeasurementCategoryId = category.Id;
            context.Measurements.Add(baseUnit);
            await context.SaveChangesAsync();

            category.BaseUnitId = baseUnit.Id;
            await context.SaveChangesAsync();

            // Act
            var result = await service.GetCategoryByIdAsync(category.Id);

            // Assert
            result.Should().NotBeNull();
            result!.Name.Should().Be("Temperature");
            result.Description.Should().Be("Temperature measurements");
            result.BaseUnitName.Should().Be("Celsius");
            result.BaseUnitSymbol.Should().Be("°C");
        }

        [Fact]
        public async Task GetCategoryByIdAsync_ShouldReturnNull_WhenCategoryDoesNotExist()
        {
            // Arrange
            using var context = new ApplicationDbContext(_dbContextOptions);
            var service = new MeasurementOrchestrationService(context, _loggerMock.Object, _httpContextAccessorMock.Object);

            // Act
            var result = await service.GetCategoryByIdAsync(999);

            // Assert
            result.Should().BeNull();
        }
    }
}

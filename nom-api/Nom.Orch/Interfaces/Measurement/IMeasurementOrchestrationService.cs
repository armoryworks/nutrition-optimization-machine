using Nom.Orch.Models.Measurement;

namespace Nom.Orch.Interfaces.Measurement
{
    /// <summary>
    /// Service interface for managing measurements and conversions.
    /// </summary>
    public interface IMeasurementOrchestrationService
    {
        /// <summary>
        /// Gets all measurements for a specific category.
        /// </summary>
        Task<List<MeasurementModel>> GetMeasurementsByCategoryAsync(long categoryId);

        /// <summary>
        /// Gets a measurement by its ID.
        /// </summary>
        Task<MeasurementModel?> GetMeasurementByIdAsync(long id);

        /// <summary>
        /// Converts a value from one measurement unit to another.
        /// </summary>
        Task<decimal> ConvertMeasurementAsync(long fromId, long toId, decimal value);

        /// <summary>
        /// Gets conversion paths between two measurement units.
        /// </summary>
        Task<List<MeasurementConversionModel>> GetConversionPathsAsync(long fromId, long toId);

        /// <summary>
        /// Gets all measurements for a specific ingredient.
        /// </summary>
        Task<List<IngredientMeasurementModel>> GetIngredientMeasurementsAsync(long ingredientId);

        /// <summary>
        /// Gets all measurements for a specific nutrient.
        /// </summary>
        Task<List<NutrientMeasurementModel>> GetNutrientMeasurementsAsync(long nutrientId);

        /// <summary>
        /// Creates a new measurement.
        /// </summary>
        Task<MeasurementModel> CreateMeasurementAsync(CreateMeasurementRequest request);

        /// <summary>
        /// Creates a new conversion rule.
        /// </summary>
        Task<MeasurementConversionModel> CreateConversionAsync(CreateConversionRequest request);

        /// <summary>
        /// Updates an existing measurement.
        /// </summary>
        Task<MeasurementModel> UpdateMeasurementAsync(long id, UpdateMeasurementRequest request);

        /// <summary>
        /// Deletes a measurement.
        /// </summary>
        Task<bool> DeleteMeasurementAsync(long id);

        /// <summary>
        /// Gets all measurement categories.
        /// </summary>
        Task<List<MeasurementCategoryModel>> GetAllCategoriesAsync();

        /// <summary>
        /// Gets a measurement category by its ID.
        /// </summary>
        Task<MeasurementCategoryModel?> GetCategoryByIdAsync(long id);
    }
}

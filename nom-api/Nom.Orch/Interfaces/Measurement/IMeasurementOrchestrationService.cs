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
        Task<List<MeasurementModel>> GetAllMeasurementsAsync();
        Task<Dictionary<long, List<MeasurementConversionModel>>> GetBulkConversionsAsync(List<(long FromId, long ToId)> conversionRequests);

        /// <summary>
        /// Gets a measurement by its ID.
        /// </summary>
        Task<MeasurementModel?> GetMeasurementByIdAsync(long id);

        /// <summary>
        /// Converts a value from one measurement unit to another.
        /// </summary>
        Task<decimal> ConvertMeasurementAsync(long fromId, long toId, decimal value);

        /// <summary>
        /// Bulk converts multiple measurement values efficiently.
        /// </summary>
        Task<List<decimal>> BulkConvertMeasurementsAsync(List<(long fromId, long toId, decimal value)> conversions);

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

        /// <summary>
        /// Creates a new ingredient-specific measurement.
        /// </summary>
        Task<IngredientMeasurementModel> CreateIngredientMeasurementAsync(CreateIngredientMeasurementRequest request);

        /// <summary>
        /// Updates an existing ingredient-specific measurement.
        /// </summary>
        Task<IngredientMeasurementModel> UpdateIngredientMeasurementAsync(long id, UpdateIngredientMeasurementRequest request);

        /// <summary>
        /// Deletes an ingredient-specific measurement.
        /// </summary>
        Task<bool> DeleteIngredientMeasurementAsync(long id);

        /// <summary>
        /// Creates a new nutrient-specific measurement.
        /// </summary>
        Task<NutrientMeasurementModel> CreateNutrientMeasurementAsync(CreateNutrientMeasurementRequest request);

        /// <summary>
        /// Updates an existing nutrient-specific measurement.
        /// </summary>
        Task<NutrientMeasurementModel> UpdateNutrientMeasurementAsync(long id, UpdateNutrientMeasurementRequest request);

        /// <summary>
        /// Deletes a nutrient-specific measurement.
        /// </summary>
        Task<bool> DeleteNutrientMeasurementAsync(long id);
    }
}

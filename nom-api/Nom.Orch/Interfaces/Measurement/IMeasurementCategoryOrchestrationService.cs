using Nom.Orch.Models.Measurement;

namespace Nom.Orch.Interfaces.Measurement
{
    /// <summary>
    /// Service interface for managing measurement categories.
    /// </summary>
    public interface IMeasurementCategoryOrchestrationService
    {
        /// <summary>
        /// Gets all measurement categories.
        /// </summary>
        Task<List<MeasurementCategoryModel>> GetAllCategoriesAsync();

        /// <summary>
        /// Gets a measurement category by its ID.
        /// </summary>
        Task<MeasurementCategoryModel?> GetCategoryByIdAsync(long id);

        /// <summary>
        /// Creates a new measurement category.
        /// </summary>
        Task<MeasurementCategoryModel> CreateCategoryAsync(CreateCategoryRequest request);

        /// <summary>
        /// Updates an existing measurement category.
        /// </summary>
        Task<bool> UpdateCategoryAsync(UpdateCategoryRequest request);

        /// <summary>
        /// Deletes a measurement category.
        /// </summary>
        Task<bool> DeleteCategoryAsync(long id);

        /// <summary>
        /// Gets all measurements in a specific category.
        /// </summary>
        Task<List<MeasurementModel>> GetMeasurementsInCategoryAsync(long categoryId);

        /// <summary>
        /// Sets the base unit for a category.
        /// </summary>
        Task<bool> SetBaseUnitAsync(long categoryId, long measurementId);
    }
}

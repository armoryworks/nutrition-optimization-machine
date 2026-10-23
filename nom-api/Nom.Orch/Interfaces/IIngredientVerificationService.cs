using System.Collections.Generic;
using System.Threading.Tasks;
using Nom.Orch.Models.Shopping;

namespace Nom.Orch.Interfaces
{
    /// <summary>
    /// Records what a household can tell us about a food without verified nutrition, and
    /// answers whether it is worth asking them. Everything recorded here is household-scoped
    /// evidence; nothing is written to the shared catalog's nutrient rows.
    /// </summary>
    public interface IIngredientVerificationService
    {
        Task<List<NeedsVerificationModel>> NeedsVerificationAsync(long householdId, IReadOnlyCollection<long> ingredientIds);
        Task<bool> SkipAsync(long ingredientId, long householdId, long personId);
        Task<NotFoodResultModel?> MarkNotFoodAsync(long ingredientId, long householdId, long personId);
        Task<LabelScanResultModel?> ScanLabelAsync(long ingredientId, long householdId, long personId, byte[] image);
        Task<IngredientVerificationModel?> GetAsync(long verificationId);
        Task<IngredientVerificationModel?> ConfirmAsync(long verificationId, long personId, IngredientVerificationConfirmModel model);
        Task<BarcodeResultModel?> RecordBarcodeAsync(long ingredientId, long householdId, long personId, string upc);
        Task<BarcodeResultModel?> RecordBarcodeFromImageAsync(long ingredientId, long householdId, long personId, byte[] image);
        Task<IngredientVerificationModel?> RecordMeasurementsAsync(long ingredientId, long householdId, long personId, IngredientMeasurementsModel model);
        Task<List<IngredientVerificationModel>> ListAsync(long householdId, long ingredientId);
        Task<(byte[] Data, string ContentType)?> GetImageAsync(long verificationId);
    }
}

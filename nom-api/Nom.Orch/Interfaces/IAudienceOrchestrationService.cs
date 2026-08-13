using System.Collections.Generic;
using System.Threading.Tasks;

namespace Nom.Orch.Interfaces
{
    public class AudienceModel
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public long OwnerPersonId { get; set; }
        public string? ManagedBy { get; set; }
        public int HouseholdCount { get; set; }
        public int RecipeCount { get; set; }
    }

    /// <summary>
    /// Audience management for audience-scoped recipe visibility (design doc
    /// §4). Owner-only mutations; audiences carrying an external ManagedBy
    /// marker are read-only to humans (the manager maintains them).
    /// </summary>
    public interface IAudienceOrchestrationService
    {
        Task<List<AudienceModel>> GetMineAsync(long personId);
        Task<AudienceModel> CreateAsync(string name, long ownerPersonId);
        Task<bool> DeleteAsync(long audienceId, long requesterPersonId);
        Task<bool> AddHouseholdAsync(long audienceId, long householdId, long requesterPersonId);
        Task<bool> RemoveHouseholdAsync(long audienceId, long householdId, long requesterPersonId);
        Task<bool> AttachRecipeAsync(long audienceId, long recipeId, long requesterPersonId);
        Task<bool> DetachRecipeAsync(long audienceId, long recipeId, long requesterPersonId);
    }
}

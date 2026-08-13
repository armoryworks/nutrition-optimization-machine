// File: Nom.Orch/Interfaces/IHouseholdOrchestrationService.cs

using System.Collections.Generic;
using System.Threading.Tasks;
using Nom.Orch.Models.Household;

namespace Nom.Orch.Interfaces
{
    public interface IHouseholdOrchestrationService
    {
        Task<List<HouseholdResponseModel>> GetAllHouseholdsAsync();
        Task<List<HouseholdResponseModel>> GetHouseholdsForMemberAsync(List<long>? householdIds);
        Task<HouseholdCreateResponseModel> CreateHouseholdAsync(HouseholdCreateModel model, long? createdByPersonId = null);
        Task<HouseholdResponseModel?> GetHouseholdAsync(long id);
        Task<HouseholdEnrollmentInfoModel?> GetEnrollmentInfoAsync(long id);
        Task<HouseholdResponseModel?> UpdateHouseholdAsync(long id, HouseholdUpdateModel model);
        Task<bool> DeleteHouseholdAsync(long id);
        Task<HouseholdInviteTokenResponseModel> CreateInviteTokenAsync(HouseholdInviteTokenCreateModel model);
        Task<HouseholdMemberResponseModel> AddMemberAsync(HouseholdMemberCreateModel model);
        Task<bool> RemoveMemberAsync(long householdId, long memberId);
        Task<HouseholdMemberResponseModel> JoinHouseholdAsync(string token, long personId);
    }
} 
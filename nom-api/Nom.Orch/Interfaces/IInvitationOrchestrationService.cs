using System.Collections.Generic;
using System.Threading.Tasks;
using Nom.Orch.Models.Person;

namespace Nom.Orch.Interfaces
{
    public interface IInvitationOrchestrationService
    {
        Task<InvitationModel> CreateInvitationAsync(CreateInvitationRequest request, long inviterPersonId);
        Task<InvitationModel> ClaimInvitationAsync(ClaimInvitationRequest request);
        Task<InvitationModel?> GetInvitationByCodeAsync(string code);
        Task<List<InvitationModel>> GetInvitationsByInviterAsync(long inviterPersonId);
        Task<List<InvitationModel>> GetInvitationsByInviteeAsync(long inviteePersonId);
        Task<bool> ValidateInvitationAsync(string code);
    }
} 
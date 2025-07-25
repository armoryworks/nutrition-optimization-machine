using Nom.Orch.Models.Communication;
using System.Threading.Tasks;

namespace Nom.Orch.Interfaces
{
    public interface ICommunicationOrchestrationService
    {
        Task<long> SendMessageAsync(SendMessageRequest request, long senderPersonId);
        // Additional methods for creating threads, getting user conversations, etc.
    }
}
using Nom.Orch.Models.Communication;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Nom.Orch.Interfaces
{
    public interface ICommunicationOrchestrationService
    {
        Task<long> SendMessageAsync(SendMessageRequest request, long senderPersonId);
        Task<List<MessageThreadResponseModel>> GetThreadsAsync(long personId);
        Task<MessageThreadResponseModel?> GetThreadAsync(long threadId, long personId);
        Task<List<MessageResponseModel>> GetMessagesAsync(long threadId, long personId);
        Task<long> CreateThreadAsync(CreateThreadRequest request, long creatorPersonId);
        Task MarkThreadAsReadAsync(long threadId, long personId);
        Task MarkMessageAsReadAsync(long messageId, long personId);
        Task DeleteThreadAsync(long threadId, long personId);
        Task ArchiveThreadAsync(long threadId, long personId);
        Task PinThreadAsync(long threadId, long personId);
        Task UnpinThreadAsync(long threadId, long personId);
        Task<List<MessageThreadResponseModel>> SearchThreadsAsync(string query, long personId);
    }
}

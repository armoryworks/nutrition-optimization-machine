using Microsoft.Extensions.Logging;
using Nom.Data;
using Nom.Data.Communication;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Communication;
using System.Threading.Tasks;

namespace Nom.Orch.Services
{
    public class CommunicationOrchestrationService : ICommunicationOrchestrationService
    {
        private readonly ApplicationDbContext _db;
        private readonly ILogger<CommunicationOrchestrationService> _logger;

        public CommunicationOrchestrationService(ApplicationDbContext db, ILogger<CommunicationOrchestrationService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<long> SendMessageAsync(SendMessageRequest request, long senderPersonId)
        {
            // Logic to verify the sender person is a participant in the thread,
            // create a new MessageEntity, and save it to the database.
            // This would also trigger an email notification.
            _logger.LogInformation("Sending message from {SenderPersonId} to thread {ThreadId}", senderPersonId, request.ThreadId);

            var message = new MessageEntity
            {
                MessageThreadId = request.ThreadId,
                SenderPersonId = senderPersonId,
                Content = request.Content
            };

            _db.Messages.Add(message);
            await _db.SaveChangesAsync();

            return message.Id;
        }
    }
}
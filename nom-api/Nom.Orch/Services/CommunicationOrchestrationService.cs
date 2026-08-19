using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nom.Data;
using Nom.Data.Communication;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Communication;
using System.Collections.Generic;
using System.Linq;
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
            _logger.LogInformation("Sending message from {SenderPersonId} to thread {ThreadId}", senderPersonId, request.ThreadId);

            // The thread id is client-supplied: only a participant may post into it.
            var isParticipant = await _db.MessageThreadParticipants
                .AnyAsync(p => p.MessageThreadId == request.ThreadId && p.PersonId == senderPersonId);
            if (!isParticipant)
                throw new UnauthorizedAccessException("You are not a participant in this conversation.");

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

        public async Task<List<MessageThreadResponseModel>> GetThreadsAsync(long personId)
        {
            var threads = await _db.MessageThreads
                .Include(t => t.Participants).ThenInclude(p => p.Person)
                .Include(t => t.Messages)
                .Where(t => t.Participants.Any(p => p.PersonId == personId))
                .AsNoTracking()
                .ToListAsync();

            return threads.Select(t => MapThread(t, personId)).ToList();
        }

        public async Task<MessageThreadResponseModel?> GetThreadAsync(long threadId, long personId)
        {
            var thread = await _db.MessageThreads
                .Include(t => t.Participants).ThenInclude(p => p.Person)
                .Include(t => t.Messages)
                .Where(t => t.Id == threadId && t.Participants.Any(p => p.PersonId == personId))
                .AsNoTracking()
                .FirstOrDefaultAsync();

            return thread == null ? null : MapThread(thread, personId);
        }

        public async Task<List<MessageResponseModel>> GetMessagesAsync(long threadId, long personId)
        {
            var isParticipant = await _db.MessageThreadParticipants
                .AnyAsync(p => p.MessageThreadId == threadId && p.PersonId == personId);

            if (!isParticipant) return new List<MessageResponseModel>();

            var messages = await _db.Messages
                .Include(m => m.SenderPerson)
                .Where(m => m.MessageThreadId == threadId)
                .OrderBy(m => m.Timestamp)
                .AsNoTracking()
                .ToListAsync();

            return messages.Select(MapMessage).ToList();
        }

        public async Task<long> CreateThreadAsync(CreateThreadRequest request, long creatorPersonId)
        {
            _logger.LogInformation("Creating thread by {CreatorPersonId} with {ParticipantCount} participants",
                creatorPersonId, request.ParticipantIds.Length);

            var thread = new MessageThreadEntity
            {
                ThreadType = request.ThreadType,
                RecipeId = request.RecipeId,
                IngredientId = request.IngredientId,
                PlanId = request.PlanId
            };
            _db.MessageThreads.Add(thread);
            await _db.SaveChangesAsync();

            // Add creator as participant
            var allParticipantIds = request.ParticipantIds.Append(creatorPersonId).Distinct();
            foreach (var pid in allParticipantIds)
            {
                _db.MessageThreadParticipants.Add(new MessageThreadParticipantEntity
                {
                    MessageThreadId = thread.Id,
                    PersonId = pid
                });
            }
            await _db.SaveChangesAsync();

            return thread.Id;
        }

        public async Task MarkThreadAsReadAsync(long threadId, long personId)
        {
            var unread = await _db.Messages
                .Where(m => m.MessageThreadId == threadId && m.SenderPersonId != personId && !m.IsRead)
                .ToListAsync();

            foreach (var msg in unread)
            {
                msg.IsRead = true;
            }
            await _db.SaveChangesAsync();
        }

        public async Task MarkMessageAsReadAsync(long messageId, long personId)
        {
            var message = await _db.Messages.FindAsync(messageId);
            if (message != null && message.SenderPersonId != personId)
            {
                message.IsRead = true;
                await _db.SaveChangesAsync();
            }
        }

        public async Task DeleteThreadAsync(long threadId, long personId)
        {
            var participant = await _db.MessageThreadParticipants
                .FirstOrDefaultAsync(p => p.MessageThreadId == threadId && p.PersonId == personId);

            if (participant != null)
            {
                _db.MessageThreadParticipants.Remove(participant);
                await _db.SaveChangesAsync();
            }
        }

        public async Task ArchiveThreadAsync(long threadId, long personId)
        {
            var participant = await _db.MessageThreadParticipants
                .FirstOrDefaultAsync(p => p.MessageThreadId == threadId && p.PersonId == personId);

            if (participant != null)
            {
                participant.IsArchived = true;
                await _db.SaveChangesAsync();
            }
        }

        public async Task PinThreadAsync(long threadId, long personId)
        {
            var participant = await _db.MessageThreadParticipants
                .FirstOrDefaultAsync(p => p.MessageThreadId == threadId && p.PersonId == personId);

            if (participant != null)
            {
                participant.IsPinned = true;
                await _db.SaveChangesAsync();
            }
        }

        public async Task UnpinThreadAsync(long threadId, long personId)
        {
            var participant = await _db.MessageThreadParticipants
                .FirstOrDefaultAsync(p => p.MessageThreadId == threadId && p.PersonId == personId);

            if (participant != null)
            {
                participant.IsPinned = false;
                await _db.SaveChangesAsync();
            }
        }

        public async Task<List<MessageThreadResponseModel>> SearchThreadsAsync(string query, long personId)
        {
            var threads = await _db.MessageThreads
                .Include(t => t.Participants).ThenInclude(p => p.Person)
                .Include(t => t.Messages)
                .Where(t => t.Participants.Any(p => p.PersonId == personId)
                    && t.Messages.Any(m => EF.Functions.ILike(m.Content, $"%{query}%")))
                .AsNoTracking()
                .ToListAsync();

            return threads.Select(t => MapThread(t, personId)).ToList();
        }

        private static MessageThreadResponseModel MapThread(MessageThreadEntity thread, long personId)
        {
            var participation = thread.Participants.FirstOrDefault(p => p.PersonId == personId);
            var lastMessage = thread.Messages.OrderByDescending(m => m.Timestamp).FirstOrDefault();

            return new MessageThreadResponseModel
            {
                Id = thread.Id,
                Participants = thread.Participants.Select(p => new MessageParticipantResponseModel
                {
                    Id = p.PersonId,
                    DisplayName = p.Person?.Name ?? string.Empty,
                    Email = string.Empty
                }).ToList(),
                LastMessage = lastMessage == null ? null : MapMessage(lastMessage),
                UnreadCount = thread.Messages.Count(m => m.SenderPersonId != personId && !m.IsRead),
                LastActivity = lastMessage?.Timestamp,
                IsArchived = participation?.IsArchived ?? false,
                IsPinned = participation?.IsPinned ?? false,
                ThreadType = thread.ThreadType,
                RecipeId = thread.RecipeId,
                IngredientId = thread.IngredientId,
                PlanId = thread.PlanId
            };
        }

        private static MessageResponseModel MapMessage(MessageEntity message)
        {
            return new MessageResponseModel
            {
                Id = message.Id,
                MessageThreadId = message.MessageThreadId,
                SenderPersonId = message.SenderPersonId,
                SenderDisplayName = message.SenderPerson?.Name ?? string.Empty,
                Content = message.Content,
                Timestamp = message.Timestamp,
                IsRead = message.IsRead
            };
        }
    }
}

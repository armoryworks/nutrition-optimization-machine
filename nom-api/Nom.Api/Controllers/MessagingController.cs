using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Communication;
using System.Threading.Tasks;

namespace Nom.Api.Controllers
{
    [Authorize]
    public class MessagingController : BaseApiController
    {
        private readonly ICommunicationOrchestrationService _communicationOrch;

        public MessagingController(ICommunicationOrchestrationService communicationOrch)
        {
            _communicationOrch = communicationOrch;
        }

        [HttpGet("threads")]
        public async Task<IActionResult> GetThreads()
        {
            var personId = GetCurrentPersonIdRequired();
            var threads = await _communicationOrch.GetThreadsAsync(personId);
            return Ok(threads);
        }

        [HttpGet("threads/{id}")]
        public async Task<IActionResult> GetThread(long id)
        {
            var personId = GetCurrentPersonIdRequired();
            var thread = await _communicationOrch.GetThreadAsync(id, personId);
            if (thread == null) return NotFound();
            return Ok(thread);
        }

        [HttpGet("threads/{id}/messages")]
        public async Task<IActionResult> GetMessages(long id)
        {
            var personId = GetCurrentPersonIdRequired();
            var messages = await _communicationOrch.GetMessagesAsync(id, personId);
            return Ok(messages);
        }

        [HttpPost("threads")]
        public async Task<IActionResult> CreateThread([FromBody] CreateThreadRequest request)
        {
            var personId = GetCurrentPersonIdRequired();
            var threadId = await _communicationOrch.CreateThreadAsync(request, personId);
            return CreatedAtAction(nameof(GetThread), new { id = threadId }, new { threadId });
        }

        [HttpPost("messages")]
        public async Task<IActionResult> SendMessage([FromBody] SendMessageRequest request)
        {
            var personId = GetCurrentPersonIdRequired();
            var messageId = await _communicationOrch.SendMessageAsync(request, personId);
            return Created($"api/messaging/threads/{request.ThreadId}/messages", new { messageId });
        }

        [HttpPatch("threads/{id}/read")]
        public async Task<IActionResult> MarkThreadAsRead(long id)
        {
            var personId = GetCurrentPersonIdRequired();
            await _communicationOrch.MarkThreadAsReadAsync(id, personId);
            return NoContent();
        }

        [HttpPatch("messages/{id}/read")]
        public async Task<IActionResult> MarkMessageAsRead(long id)
        {
            var personId = GetCurrentPersonIdRequired();
            await _communicationOrch.MarkMessageAsReadAsync(id, personId);
            return NoContent();
        }

        [HttpDelete("threads/{id}")]
        public async Task<IActionResult> DeleteThread(long id)
        {
            var personId = GetCurrentPersonIdRequired();
            await _communicationOrch.DeleteThreadAsync(id, personId);
            return NoContent();
        }

        [HttpPatch("threads/{id}/archive")]
        public async Task<IActionResult> ArchiveThread(long id)
        {
            var personId = GetCurrentPersonIdRequired();
            await _communicationOrch.ArchiveThreadAsync(id, personId);
            return NoContent();
        }

        [HttpPatch("threads/{id}/pin")]
        public async Task<IActionResult> PinThread(long id)
        {
            var personId = GetCurrentPersonIdRequired();
            await _communicationOrch.PinThreadAsync(id, personId);
            return NoContent();
        }

        [HttpPatch("threads/{id}/unpin")]
        public async Task<IActionResult> UnpinThread(long id)
        {
            var personId = GetCurrentPersonIdRequired();
            await _communicationOrch.UnpinThreadAsync(id, personId);
            return NoContent();
        }

        [HttpGet("threads/search")]
        public async Task<IActionResult> SearchThreads([FromQuery] string query)
        {
            var personId = GetCurrentPersonIdRequired();
            var threads = await _communicationOrch.SearchThreadsAsync(query ?? string.Empty, personId);
            return Ok(threads);
        }
    }
}

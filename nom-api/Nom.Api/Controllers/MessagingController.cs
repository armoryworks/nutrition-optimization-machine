using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Communication;
using System;
using System.Threading.Tasks;

namespace Nom.Api.Controllers
{
    [Authorize]
    public class MessagingController : BaseApiController
    {
        private readonly ILogger<MessagingController> _logger;
        private readonly ICommunicationOrchestrationService _communicationOrch;

        public MessagingController(ILogger<MessagingController> logger, ICommunicationOrchestrationService communicationOrch)
        {
            _logger = logger;
            _communicationOrch = communicationOrch;
        }

        [HttpGet("threads")]
        public async Task<IActionResult> GetThreads()
        {
            try
            {
                var personId = GetCurrentPersonIdRequired();
                var threads = await _communicationOrch.GetThreadsAsync(personId);
                return Ok(threads);
            }
            catch (UnauthorizedAccessException)
            {
                return Unauthorized("User profile not complete.");
            }
        }

        [HttpGet("threads/{id}")]
        public async Task<IActionResult> GetThread(long id)
        {
            try
            {
                var personId = GetCurrentPersonIdRequired();
                var thread = await _communicationOrch.GetThreadAsync(id, personId);
                if (thread == null) return NotFound();
                return Ok(thread);
            }
            catch (UnauthorizedAccessException)
            {
                return Unauthorized("User profile not complete.");
            }
        }

        [HttpGet("threads/{id}/messages")]
        public async Task<IActionResult> GetMessages(long id)
        {
            try
            {
                var personId = GetCurrentPersonIdRequired();
                var messages = await _communicationOrch.GetMessagesAsync(id, personId);
                return Ok(messages);
            }
            catch (UnauthorizedAccessException)
            {
                return Unauthorized("User profile not complete.");
            }
        }

        [HttpPost("threads")]
        public async Task<IActionResult> CreateThread([FromBody] CreateThreadRequest request)
        {
            try
            {
                var personId = GetCurrentPersonIdRequired();
                var threadId = await _communicationOrch.CreateThreadAsync(request, personId);
                return Ok(new { threadId });
            }
            catch (UnauthorizedAccessException)
            {
                return Unauthorized("User profile not complete.");
            }
        }

        [HttpPost("messages")]
        public async Task<IActionResult> SendMessage([FromBody] SendMessageRequest request)
        {
            try
            {
                var personId = GetCurrentPersonIdRequired();
                var messageId = await _communicationOrch.SendMessageAsync(request, personId);
                return Ok(new { messageId });
            }
            catch (UnauthorizedAccessException)
            {
                return Unauthorized("User profile not complete.");
            }
        }

        [HttpPatch("threads/{id}/read")]
        public async Task<IActionResult> MarkThreadAsRead(long id)
        {
            try
            {
                var personId = GetCurrentPersonIdRequired();
                await _communicationOrch.MarkThreadAsReadAsync(id, personId);
                return NoContent();
            }
            catch (UnauthorizedAccessException)
            {
                return Unauthorized("User profile not complete.");
            }
        }

        [HttpPatch("messages/{id}/read")]
        public async Task<IActionResult> MarkMessageAsRead(long id)
        {
            try
            {
                var personId = GetCurrentPersonIdRequired();
                await _communicationOrch.MarkMessageAsReadAsync(id, personId);
                return NoContent();
            }
            catch (UnauthorizedAccessException)
            {
                return Unauthorized("User profile not complete.");
            }
        }

        [HttpDelete("threads/{id}")]
        public async Task<IActionResult> DeleteThread(long id)
        {
            try
            {
                var personId = GetCurrentPersonIdRequired();
                await _communicationOrch.DeleteThreadAsync(id, personId);
                return NoContent();
            }
            catch (UnauthorizedAccessException)
            {
                return Unauthorized("User profile not complete.");
            }
        }

        [HttpPatch("threads/{id}/archive")]
        public async Task<IActionResult> ArchiveThread(long id)
        {
            try
            {
                var personId = GetCurrentPersonIdRequired();
                await _communicationOrch.ArchiveThreadAsync(id, personId);
                return NoContent();
            }
            catch (UnauthorizedAccessException)
            {
                return Unauthorized("User profile not complete.");
            }
        }

        [HttpPatch("threads/{id}/pin")]
        public async Task<IActionResult> PinThread(long id)
        {
            try
            {
                var personId = GetCurrentPersonIdRequired();
                await _communicationOrch.PinThreadAsync(id, personId);
                return NoContent();
            }
            catch (UnauthorizedAccessException)
            {
                return Unauthorized("User profile not complete.");
            }
        }

        [HttpPatch("threads/{id}/unpin")]
        public async Task<IActionResult> UnpinThread(long id)
        {
            try
            {
                var personId = GetCurrentPersonIdRequired();
                await _communicationOrch.UnpinThreadAsync(id, personId);
                return NoContent();
            }
            catch (UnauthorizedAccessException)
            {
                return Unauthorized("User profile not complete.");
            }
        }

        [HttpGet("threads/search")]
        public async Task<IActionResult> SearchThreads([FromQuery] string query)
        {
            try
            {
                var personId = GetCurrentPersonIdRequired();
                var threads = await _communicationOrch.SearchThreadsAsync(query ?? string.Empty, personId);
                return Ok(threads);
            }
            catch (UnauthorizedAccessException)
            {
                return Unauthorized("User profile not complete.");
            }
        }
    }
}

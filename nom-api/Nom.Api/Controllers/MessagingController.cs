// File: Nom.Api/Controllers/MessagingController.cs

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Communication;
using System;
using System.Threading.Tasks;

namespace Nom.Api.Controllers
{
    [Authorize] // All actions require an authenticated user.
    public class MessagingController : BaseApiController
    {
        private readonly ILogger<MessagingController> _logger;
        private readonly ICommunicationOrchestrationService _communicationOrch;

        public MessagingController(ILogger<MessagingController> logger, ICommunicationOrchestrationService communicationOrch)
        {
            _logger = logger;
            _communicationOrch = communicationOrch;
        }

        [HttpPost("send")]
        public async Task<IActionResult> SendMessage([FromBody] SendMessageRequest request)
        {
            try
            {
                var senderPersonId = GetCurrentPersonIdRequired();
                var message = await _communicationOrch.SendMessageAsync(request, senderPersonId);
                return Ok(message);
            }
            catch (UnauthorizedAccessException)
            {
                return Unauthorized("User profile not complete. Please complete registration first.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending message from person {PersonId} to thread {ThreadId}", GetCurrentPersonIdRequired(), request.ThreadId);
                return StatusCode(500, "An error occurred while sending the message");
            }
        }
    }
}
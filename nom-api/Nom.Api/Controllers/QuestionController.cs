// Nom.Api/Controllers/QuestionController.cs
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Nom.Api.Models.Question; // For QuestionResponseModel, AnswerSubmissionItemModel, QuestionAnswerSubmissionModel
using Nom.Orch.Interfaces; // For IQuestionOrchestrationService
using Nom.Orch.Models.Question; // For AnswerOrchestrationModel
using Nom.Orch.Enums; // ADDED: To use AnswerTypeEnum for mapping
using Microsoft.Extensions.Logging;
using System;
using Microsoft.AspNetCore.Authorization; // For Exception

namespace Nom.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class QuestionController : ControllerBase
    {
        private readonly IQuestionOrchestrationService _questionOrchestrationService;
        private readonly IPersonOrchestrationService _personOrchestrationService;
        private readonly ILogger<QuestionController> _logger;

        public QuestionController(
            IQuestionOrchestrationService questionOrchestrationService,
            IPersonOrchestrationService personOrchestrationService,
            ILogger<QuestionController> logger)
        {
            _questionOrchestrationService = questionOrchestrationService;
            _personOrchestrationService = personOrchestrationService;
            _logger = logger;
        }

        /// <summary>
        /// Retrieves the list of questions required for initial user onboarding or plan creation.
        /// </summary>
        /// <returns>A list of onboarding questions.</returns>
        [HttpGet("onboarding")] // GET /api/Question/onboarding
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<IEnumerable<QuestionResponseModel>>> GetOnboardingQuestions()
        {
            try
            {
                var orchestrationModels = await _questionOrchestrationService.GetRequiredOnboardingQuestionsAsync();

                // Map orchestration models to API response models
                var responseModels = orchestrationModels.Select(q => new QuestionResponseModel
                {
                    Id = q.Id,
                    Text = q.Text,
                    Hint = q.Hint,
                    QuestionCategoryId = q.QuestionCategoryId,
                    AnswerType = q.AnswerType.ToString(), // UPDATED: Just convert AnswerTypeEnum to string
                    DisplayOrder = q.DisplayOrder,
                    IsActive = q.IsActive,
                    DefaultAnswer = q.DefaultAnswer,
                    ValidationRegex = q.ValidationRegex,
                    Options = q.Options
                }).ToList();

                _logger.LogInformation("Successfully retrieved {Count} onboarding questions.", responseModels.Count);
                return Ok(responseModels);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving onboarding questions.");
                return StatusCode(StatusCodes.Status500InternalServerError, "An error occurred while retrieving questions.");
            }
        }

        /// <summary>
        /// Submits a collection of answers for a person's onboarding questions.
        /// </summary>
        /// <param name="model">The model containing a list of answers.</param>
        /// <returns>A status indicating whether the answers were successfully submitted.</returns>
        [HttpPost("answers")] // POST /api/Question/answers
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> SubmitOnboardingAnswers([FromBody] QuestionAnswerSubmissionModel model)
        {
            if (!ModelState.IsValid)
            {
                _logger.LogWarning("SubmitOnboardingAnswers: Invalid ModelState.");
                return BadRequest(ModelState);
            }

            // Get the person ID from the user's claims or context
            var personId = GetPersonIdFromUserContext();

            if (personId == 0)
            {
                _logger.LogWarning("SubmitOnboardingAnswers: Unable to determine the current user's Person ID.");
                return Unauthorized(new { Message = "Unable to determine the current user's Person ID." });
            }

            try
            {
                var answers = model.Answers.Select(a => new AnswerOrchestrationModel
                {
                    QuestionId = a.QuestionId,
                    SubmittedAnswer = a.SubmittedAnswer
                }).ToList();

                var success = await _questionOrchestrationService.SubmitOnboardingAnswersAsync(personId, answers);

                if (success)
                {
                    _logger.LogInformation("SubmitOnboardingAnswers: Successfully submitted answers for Person ID: {PersonId}", personId);
                    return Ok(new { Message = "Answers submitted successfully." });
                }
                else
                {
                    _logger.LogError("SubmitOnboardingAnswers: Failed to process or save answers for Person ID: {PersonId}. Check previous logs for details.", personId);
                    return StatusCode(StatusCodes.Status500InternalServerError, new { Message = "Failed to process answers. Please check input and try again." });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SubmitOnboardingAnswers: An unexpected error occurred while submitting answers for Person ID: {PersonId}", personId);
                return StatusCode(StatusCodes.Status500InternalServerError, new { Message = "An unexpected error occurred." });
            }
        }

        private long GetPersonIdFromUserContext()
        {
            return this._personOrchestrationService.GetCurrentPersonId();
        }
    }
}
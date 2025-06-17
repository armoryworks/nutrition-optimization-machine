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
using System; // For Exception

namespace Nom.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class QuestionController : ControllerBase
    {
        private readonly IQuestionOrchestrationService _questionOrchestrationService;
        private readonly ILogger<QuestionController> _logger;

        public QuestionController(
            IQuestionOrchestrationService questionOrchestrationService,
            ILogger<QuestionController> logger)
        {
            _questionOrchestrationService = questionOrchestrationService;
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
                    IsRequiredForPlanCreation = q.IsRequiredForPlanCreation,
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
        /// <param name="submissionModel">The model containing the Person ID and a list of answers.</param>
        /// <returns>A status indicating whether the answers were successfully submitted.</returns>
        [HttpPost("answers")] // POST /api/Question/answers
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> SubmitOnboardingAnswers([FromBody] QuestionAnswerSubmissionModel submissionModel)
        {
            if (!ModelState.IsValid)
            {
                _logger.LogWarning("SubmitOnboardingAnswers: Invalid ModelState for Person ID: {PersonId}", submissionModel.PersonId);
                return BadRequest(ModelState);
            }

            try
            {
                // Map API input answers to orchestration-level answer models
                var orchestrationAnswers = submissionModel.Answers.Select(a => new AnswerOrchestrationModel
                {
                    QuestionId = a.QuestionId,
                    SubmittedAnswer = a.SubmittedAnswer
                }).ToList();

                var success = await _questionOrchestrationService.SubmitOnboardingAnswersAsync(
                    submissionModel.PersonId,
                    orchestrationAnswers
                );

                if (success)
                {
                    _logger.LogInformation("SubmitOnboardingAnswers: Successfully submitted answers for Person ID: {PersonId}", submissionModel.PersonId);
                    return Ok(new { Message = "Answers submitted successfully." });
                }
                else
                {
                    _logger.LogError("SubmitOnboardingAnswers: Failed to process or save answers for Person ID: {PersonId}. Check previous logs for details.", submissionModel.PersonId);
                    ModelState.AddModelError(string.Empty, "Failed to process answers. Please check input and try again.");
                    return StatusCode(StatusCodes.Status500InternalServerError, ModelState);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SubmitOnboardingAnswers: An unexpected error occurred while submitting answers for Person ID: {PersonId}", submissionModel.PersonId);
                ModelState.AddModelError(string.Empty, "An unexpected error occurred.");
                return StatusCode(StatusCodes.Status500InternalServerError, ModelState);
            }
        }
    }
}
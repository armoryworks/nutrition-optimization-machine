// Nom.Orch/Interfaces/IQuestionOrchestrationService.cs
using System.Collections.Generic;
using System.Threading.Tasks;
using Nom.Orch.Models.Question; // For orchestration-level question and answer models

namespace Nom.Orch.Interfaces
{
    /// <summary>
    /// Defines the business logic operations for managing questions and answers.
    /// </summary>
    public interface IQuestionOrchestrationService
    {
        /// <summary>
        /// Retrieves a list of questions that are required for initial user onboarding or plan creation.
        /// </summary>
        /// <returns>A list of QuestionOrchestrationModel containing relevant questions.</returns>
        Task<List<QuestionOrchestrationModel>> GetRequiredOnboardingQuestionsAsync();

        /// <summary>
        /// Submits and processes a collection of answers for a given person.
        /// Performs validation against question types and stores the answers.
        /// </summary>
        /// <param name="personId">The ID of the person submitting the answers.</param>
        /// <param name="answers">A list of AnswerOrchestrationModel containing the question ID and submitted answer.</param>
        /// <returns>True if all answers were successfully processed and saved; otherwise, false.</returns>
        Task<bool> SubmitOnboardingAnswersAsync(long personId, List<AnswerOrchestrationModel> answers);
    }
}
// Nom.Orch/Models/Question/AnswerOrchestrationModel.cs
namespace Nom.Orch.Models.Question
{
    /// <summary>
    /// Represents a single answer item for processing within the orchestration layer.
    /// </summary>
    public class AnswerOrchestrationModel
    {
        public long QuestionId { get; set; }
        public string? SubmittedAnswer { get; set; }
    }
}
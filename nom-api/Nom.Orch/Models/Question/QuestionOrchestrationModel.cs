// Nom.Orch/Models/Question/QuestionOrchestrationModel.cs
using System.Collections.Generic;
using Nom.Orch.Enums;
using Nom.Orch.Enums.Question; // ADDED: To recognize AnswerTypeEnum

namespace Nom.Orch.Models.Question
{
    /// <summary>
    /// Represents a question as processed and structured by the orchestration layer.
    /// </summary>
    public class QuestionOrchestrationModel
    {
        public long Id { get; set; }
        public required string Text { get; set; }
        public string? Hint { get; set; }
        public long QuestionCategoryId { get; set; }
        public AnswerTypeEnum AnswerType { get; set; } // UPDATED: Changed type to AnswerTypeEnum
        public int DisplayOrder { get; set; }
        public bool IsActive { get; set; }
        public bool IsRequiredForPlanCreation { get; set; }
        public string? DefaultAnswer { get; set; } // For Yes/No, single-select defaults
        public string? ValidationRegex { get; set; } // Regex for text input validation
        public List<string>? Options { get; set; } // For multi-select/single-select options
    }
}
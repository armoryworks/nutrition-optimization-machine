// Nom.Api/Models/Question/AnswerSubmissionItemModel.cs
using System.ComponentModel.DataAnnotations;

namespace Nom.Api.Models.Question
{
    /// <summary>
    /// Represents a single answer submitted for a question.
    /// </summary>
    public class AnswerSubmissionItemModel
    {
        /// <summary>
        /// The ID of the question this answer corresponds to.
        /// </summary>
        [Required(ErrorMessage = "Question ID is required for each answer.")]
        public long QuestionId { get; set; }

        /// <summary>
        /// The submitted answer value. This can be a string (for text),
        /// "true"/"false" (for Yes/No), or a JSON string (for multi-select options).
        /// </summary>
        public string? SubmittedAnswer { get; set; } // Can be null for optional questions, or empty string for blank text answers
    }
}
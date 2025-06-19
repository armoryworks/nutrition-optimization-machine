// Nom.Api/Models/Question/QuestionAnswerSubmissionModel.cs
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Nom.Api.Models.Question
{
    /// <summary>
    /// Represents the collection of answers submitted by a person for onboarding questions.
    /// </summary>
    public class QuestionAnswerSubmissionModel
    {
        /// <summary>
        /// The list of answers submitted by the person.
        /// </summary>
        [Required(ErrorMessage = "At least one answer must be submitted.")]
        [MinLength(1, ErrorMessage = "At least one answer must be submitted.")]
        public required List<AnswerSubmissionItemModel> Answers { get; set; }
    }
}
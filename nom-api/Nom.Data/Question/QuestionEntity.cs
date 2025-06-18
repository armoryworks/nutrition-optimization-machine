using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Nom.Data.Reference; // For QuestionCategory and AnswerType references

namespace Nom.Data.Question
{
    /// <summary>
    /// Represents a single question posed to the user for dietary preferences/restrictions.
    /// Maps to the 'question.Question' table.
    /// </summary>
    [Table("Question", Schema = "question")]
    public class QuestionEntity : BaseEntity
    {
        [Required]
        [MaxLength(500)]
        public required string Text { get; set; }

        [MaxLength(1000)]
        public string? Hint { get; set; } // Optional helper text for the question

        /// <summary>
        /// Foreign key to the GroupEntity that categorizes this question (e.g., Societal, Medical).
        /// </summary>
        [Required]
        public long QuestionCategoryId { get; set; }
        [ForeignKey(nameof(QuestionCategoryId))]
        public virtual GroupEntity QuestionCategory { get; set; } = default!;

        /// <summary>
        /// Foreign key to a ReferenceEntity that defines the expected answer type
        /// (e.g., "Yes/No", "Text Input", "Multi-Select").
        /// </summary>
        [Required]
        public long AnswerTypeRefId { get; set; }
        [ForeignKey(nameof(AnswerTypeRefId))]
        public virtual ReferenceEntity AnswerType { get; set; } = default!;

        /// <summary>
        /// Display order of the question within its category.
        /// </summary>
        [Required]
        public int DisplayOrder { get; set; }

        /// <summary>
        /// Indicates if the question is active and should be displayed.
        /// </summary>
        [Required]
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// Indicates if answering this question is required to proceed with plan creation.
        /// </summary>
        [Required]
        public bool IsRequiredForPlanCreation { get; set; } = false;

        /// <summary>
        /// Optional default answer value, useful for Yes/No toggles or pre-filling.
        /// Can be JSON for complex types (e.g., default multi-select options).
        /// </summary>
        [MaxLength(2047)]
        public string? DefaultAnswer { get; set; }

        /// <summary>
        /// Optional regex for validating text-based answers.
        /// </summary>
        [MaxLength(500)]
        public string? ValidationRegex { get; set; }

        /// <summary>
        /// Defines the next question in the workflow if the answer to this question is true.
        /// Only relevant for boolean answers.
        /// </summary>
        public long? NextQuestionOnTrue { get; set; } // Optional workflow question ID

        /// <summary>
        /// Defines the next question in the workflow if the answer to this question is false.
        /// Only relevant for boolean answers.
        /// </summary>
        public long? NextQuestionOnFalse { get; set; } // Optional workflow question ID
    }
}
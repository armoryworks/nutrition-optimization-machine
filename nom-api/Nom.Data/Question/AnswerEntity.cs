using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Nom.Data.Plan;    // For PlanEntity reference
using Nom.Data.Person;  // For PersonEntity reference

namespace Nom.Data.Question
{
    /// <summary>
    /// Stores a user's answer to a specific question, linked to a plan and optionally a person.
    /// Maps to the 'question.Answer' table.
    /// </summary>
    [Table("Answer", Schema = "question")]
    public class AnswerEntity : BaseEntity
    {
        /// <summary>
        /// Foreign key to the QuestionEntity that this answer corresponds to.
        /// </summary>
        [Required]
        public long QuestionId { get; set; }
        [ForeignKey(nameof(QuestionId))]
        public virtual QuestionEntity Question { get; set; } = default!;

        /// <summary>
        /// Foreign key to the PlanEntity that this answer is associated with.
        /// </summary>
        [Required]
        public long PlanId { get; set; }
        [ForeignKey(nameof(PlanId))]
        public virtual PlanEntity Plan { get; set; } = default!;

        /// <summary>
        /// Optional foreign key to the PersonEntity who provided this answer (if specific to an individual in the plan).
        /// </summary>
        public long? PersonId { get; set; }
        [ForeignKey(nameof(PersonId))]
        public virtual PersonEntity? Person { get; set; }

        /// <summary>
        /// The actual answer provided by the user, stored as text.
        /// For complex answer types (multi-select), this could be a JSON string.
        /// </summary>
        [Required]
        [MaxLength(4000)] // Sufficient length for text or JSON answers
        public required string AnswerText { get; set; }

        /// <summary>
        /// Timestamp when the answer was recorded.
        /// </summary>
        [Required]
        public DateTime AnsweredDate { get; set; } = DateTime.UtcNow; // Default to UTC now
    }
}
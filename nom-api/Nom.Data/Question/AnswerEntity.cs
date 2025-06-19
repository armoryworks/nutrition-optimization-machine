// Nom.Data/Question/AnswerEntity.cs
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Nom.Data.Person; // For PersonEntity navigation
using Nom.Data.Plan; // For PlanEntity navigation
using Nom.Data.Reference; // For QuestionEntity navigation

namespace Nom.Data.Question
{
    [Table("Answer", Schema = "question")]
    public class AnswerEntity : BaseEntity // Inherits Id only from BaseEntity
    {
        [Required]
        public long QuestionId { get; set; }

        [ForeignKey(nameof(QuestionId))]
        public virtual QuestionEntity Question { get; set; } = default!;

        // --- NEW/UPDATED: Fields for the actual answer and its specific audit info ---
        [Required] // An answer must have a value
        [MaxLength(4000)] // Sufficient length for text or JSON array strings
        public required string AnswerText { get; set; } // Renamed from SubmittedAnswer for clarity

        [Required]
        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

        [Required]
        public long CreatedByPersonId { get; set; }

        [ForeignKey(nameof(CreatedByPersonId))]
        public virtual PersonEntity CreatedByPerson { get; set; } = default!; // The person who created/submitted this answer
        // --- END NEW/UPDATED ---
    }
}
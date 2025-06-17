using Microsoft.AspNetCore.Identity; // Assuming IdentityUser is used here
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Nom.Data.Plan;
using Nom.Data.Question;
using Nom.Data.Audit; // For PlanEntity and PlanParticipantEntity

namespace Nom.Data.Person
{
    /// <summary>
    /// Represents a person in the system, distinct from their Identity user account.
    /// A person can be an administrator, a plan participant, or a recipient of notifications.
    /// </summary>
    [Table("Person", Schema = "person")]
    public class PersonEntity : BaseEntity
    {
        /// <summary>
        /// The display name for the person within the application (e.g., "John Doe", "Mom", "My Admin").
        /// </summary>
        [Required]
        [MaxLength(255)]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Optional: Foreign key to the ASP.NET Core Identity user ID.
        /// Null if the person is an "unregistered" participant (e.g., a child) or the "System" person.
        /// </summary>
        public string? UserId { get; set; } // Matches IdentityUser.Id type (typically string)

        /// <summary>
        /// Optional: The invitation code associated with this person, if they are an invited participant.
        /// This code is used when an invited person registers and claims their pre-existing Person record.
        /// Should be unique if not null.
        /// </summary>
        [MaxLength(50)]
        public string? InvitationCode { get; set; } // For invited users to claim this Person record

        // Navigation properties for relationships where this person is the 'CreatedByPerson'
        public virtual ICollection<PlanEntity> PlansAdministering { get; set; } = new List<PlanEntity>();
        public virtual ICollection<AnswerEntity> AnswersCreated { get; set; } = new List<AnswerEntity>();
        public virtual ICollection<AuditLogEntryEntity> AuditLogEntriesCreated { get; set; } = new List<AuditLogEntryEntity>();

        // NEW: Navigation property for participations this person is part of
        public virtual ICollection<PlanParticipantEntity> PlanParticipations { get; set; } = new List<PlanParticipantEntity>();

        // NEW: Navigation property for PlanParticipant records this person created (e.g., an admin adding participants)
        public virtual ICollection<PlanParticipantEntity> CreatedPlanParticipations { get; set; } = new List<PlanParticipantEntity>();

        // Other attributes can be added here or via a PersonAttributeEntity for extensibility
        public virtual ICollection<PersonAttributeEntity> Attributes { get; set; } = new List<PersonAttributeEntity>();
    }
}

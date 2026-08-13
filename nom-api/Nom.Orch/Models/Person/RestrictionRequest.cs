using System.ComponentModel.DataAnnotations;

namespace Nom.Orch.Models.Person
{
    /// <summary>
    /// DTO for capturing a single restriction during onboarding.
    /// Corresponds to RestrictionEntity.
    /// Note: PersonId/PlanId will be set by orchestration service based on context.
    /// </summary>
    public class RestrictionRequest
    {
        [Required(ErrorMessage = "Restriction Name is required.")]
        [MaxLength(200, ErrorMessage = "Restriction Name cannot exceed 200 characters.")]
        public string Name { get; set; } = string.Empty;

        public string? Description { get; set; }

        [Required(ErrorMessage = "Restriction Type ID is required.")]
        public long RestrictionTypeId { get; set; } // Reference to a RestrictionType in Reference Data

        // New properties for conditional restriction allocation
        public bool AppliesToEntirePlan { get; set; } = false; // Indicates if this restriction applies to the whole plan
        public List<long>? AffectedPersonIds { get; set; } // List of Person IDs if AppliesToEntirePlan is false

        /// <summary>
        /// Restriction id when this DTO is returned as part of existing data
        /// (e.g. onboarding state). Ignored on input.
        /// </summary>
        public long? Id { get; set; }

        /// <summary>
        /// True when a household steward (or external manager) has locked this
        /// restriction; locked restrictions cannot be removed by the member.
        /// Server-controlled — ignored on input.
        /// </summary>
        public bool Locked { get; set; }

        /// <summary>
        /// Who locked the restriction ("person:{id}" or an external manager
        /// marker such as "brigade:{id}"). Server-controlled — ignored on input.
        /// </summary>
        public string? LockedBy { get; set; }
    }
}

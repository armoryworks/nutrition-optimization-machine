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
    }
}

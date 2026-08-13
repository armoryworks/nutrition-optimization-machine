// File: Nom.Orch/Models/Household/HouseholdConvertModel.cs

using System.ComponentModel.DataAnnotations;

namespace Nom.Orch.Models.Household
{
    /// <summary>
    /// Converts a personal kitchen into a shared household (the side effect
    /// of the first invite): renames it and clears the personal flag.
    /// </summary>
    public class HouseholdConvertModel
    {
        [Required(ErrorMessage = "Household name is required.")]
        [StringLength(255, ErrorMessage = "Household name cannot exceed 255 characters.")]
        public required string Name { get; set; }
    }
}

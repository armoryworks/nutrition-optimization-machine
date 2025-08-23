// File: Nom.Orch/Models/Shopping/ShoppingListCreateModel.cs

using System.ComponentModel.DataAnnotations;

namespace Nom.Orch.Models.Shopping
{
    public class ShoppingListCreateModel
    {
        [Required(ErrorMessage = "Shopping list name is required.")]
        [StringLength(255, ErrorMessage = "Shopping list name cannot exceed 255 characters.")]
        public required string Name { get; set; }

        [StringLength(2047, ErrorMessage = "Description cannot exceed 2047 characters.")]
        public string? Description { get; set; }

        // public long AuthorId { get; set; } // REMOVED - Will be set from claims

        public long? HouseholdId { get; set; }

        public long? GroupId { get; set; }
    }
} 
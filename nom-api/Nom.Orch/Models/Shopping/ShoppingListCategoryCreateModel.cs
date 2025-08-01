using System.ComponentModel.DataAnnotations;

namespace Nom.Orch.Models.Shopping
{
    public class ShoppingListCategoryCreateModel
    {
        [Required(ErrorMessage = "Category name is required.")]
        [StringLength(255, ErrorMessage = "Category name cannot exceed 255 characters.")]
        public required string Name { get; set; }
        
        [StringLength(2047, ErrorMessage = "Description cannot exceed 2047 characters.")]
        public string? Description { get; set; }
        
        public int SortOrder { get; set; } = 0;
        
        public string? Color { get; set; }
    }
} 
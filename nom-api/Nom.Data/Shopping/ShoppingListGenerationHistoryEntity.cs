using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace Nom.Data.Shopping
{
    [Table("ShoppingListGenerationHistory", Schema = "shopping")]
    public class ShoppingListGenerationHistoryEntity : BaseEntity
    {
        [Required]
        public long ShoppingListId { get; set; }

        [Required]
        public DateTime GeneratedDate { get; set; }

        [Required]
        [MaxLength(50)]
        public string GenerationMethod { get; set; } = string.Empty;

        [Required]
        public int RecipeCount { get; set; }

        [Required]
        public int ItemCount { get; set; }

        [Column(TypeName = "decimal(10,2)")]
        public decimal? EstimatedCost { get; set; }

        [Required]
        public bool OptimizationApplied { get; set; }

        [Column(TypeName = "text")]
        public string? OptimizationDetails { get; set; }

        [Column(TypeName = "text")]
        public string? GeneratedItems { get; set; }

        [Column(TypeName = "text")]
        public string? ExcludedItems { get; set; }

        [Column(TypeName = "text")]
        public string? SubstitutionsApplied { get; set; }
    }
}
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Nom.Data.Reference; // For StepType

namespace Nom.Data.Recipe
{
    [Table("RecipeStep", Schema = "recipe")] // Adjusted: Table name PascalCase, schema lowercase
    public class RecipeStepEntity : BaseEntity
    {
        [Required]
        public long RecipeId { get; set; }
        [ForeignKey(nameof(RecipeId))]
        public virtual RecipeEntity Recipe { get; set; } = default!;

        public long? StepTypeId { get; set; } // NULLable in SQL
        [ForeignKey(nameof(StepTypeId))]
        public virtual ReferenceEntity? StepType { get; set; }

        [Required]
        [MaxLength(255)]
        public string Summary { get; set; } = string.Empty;

        [Required]
        public byte StepNumber { get; set; } // TINYINT in SQL maps to byte in C#

        [Required]
        [MaxLength(2047)]
        public string Description { get; set; } = string.Empty;
    }
}
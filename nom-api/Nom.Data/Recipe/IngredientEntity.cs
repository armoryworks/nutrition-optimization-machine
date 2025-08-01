// File: Nom.Data/Recipe/IngredientEntity.cs

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Nom.Data.Nutrient;
using Nom.Data.Person;
using Nom.Data.Reference;

namespace Nom.Data.Recipe
{
    [Table("Ingredient", Schema = "recipe")]
    public class IngredientEntity : BaseEntity
    {
        [Required]
        [MaxLength(2047)]
        public string Name { get; set; } = string.Empty;

        [Column(TypeName = "text")]
        public string? Description { get; set; }

        // Mealie-specific fields
        [MaxLength(2047)]
        public string? PluralName { get; set; }

        [MaxLength(50)]
        public string? FdcId { get; set; }

        [MaxLength(255)]
        public string FdcDataType { get; set; } = string.Empty;

        // Normalized search fields (from Mealie)
        [MaxLength(2047)]
        public string? NameNormalized { get; set; }

        [MaxLength(2047)]
        public string? PluralNameNormalized { get; set; }

        // Curation and ownership
        [Required]
        public long CurationStatusId { get; set; }
        [ForeignKey(nameof(CurationStatusId))]
        public virtual ReferenceEntity? CurationStatus { get; set; }

        public long? AuthorId { get; set; }
        [ForeignKey(nameof(AuthorId))]
        public virtual PersonEntity? Author { get; set; }

        // Label association (from Mealie)
        public long? LabelId { get; set; }
        [ForeignKey(nameof(LabelId))]
        public virtual ReferenceEntity? Label { get; set; }

        // Legacy field (from Mealie)
        public bool? OnHand { get; set; } = false;

        // Navigation properties
        public virtual ICollection<IngredientNutrientEntity> IngredientNutrients { get; set; } = new List<IngredientNutrientEntity>();
        public virtual ICollection<IngredientAliasEntity> Aliases { get; set; } = new List<IngredientAliasEntity>();
        
        // New navigation properties (from Mealie)
        public virtual ICollection<RecipeIngredientEntity> RecipeIngredients { get; set; } = new List<RecipeIngredientEntity>();
        public virtual ICollection<IngredientExtrasEntity> Extras { get; set; } = new List<IngredientExtrasEntity>();
    }
}
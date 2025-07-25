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

        [MaxLength(50)]
        public string? FdcId { get; set; }

        [MaxLength(255)]
        public string FdcDataType { get; set; } = string.Empty;

        [Required]
        public long CurationStatusId { get; set; }
        [ForeignKey(nameof(CurationStatusId))]
        public virtual ReferenceEntity? CurationStatus { get; set; }

        public long? AuthorId { get; set; }
        [ForeignKey(nameof(AuthorId))]
        public virtual PersonEntity? Author { get; set; }

        public virtual ICollection<IngredientNutrientEntity> IngredientNutrients { get; set; } = new List<IngredientNutrientEntity>();
        public virtual ICollection<IngredientAliasEntity> Aliases { get; set; } = new List<IngredientAliasEntity>();
    }
}
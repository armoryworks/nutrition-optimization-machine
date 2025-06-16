using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Nom.Data.Reference;
using Nom.Data.Person;

namespace Nom.Data.Recipe
{
    [Table("Recipe", Schema = "recipe")]
    public class RecipeEntity : BaseEntity
    {
        [Required]
        [MaxLength(255)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(2047)]
        public string? Description { get; set; }

        [MaxLength(2047)]
        public string? Instructions { get; set; }

        public decimal? Quantity { get; set; }

        public long? QuantityMeasurementTypeId { get; set; }
        [ForeignKey(nameof(QuantityMeasurementTypeId))]
        public virtual ReferenceEntity? QuantityMeasurementType { get; set; }

        // Foreign Keys and Navigation Properties for Creator and Curator (1-M from Person)
        [Required]
        public long CreatedById { get; set; }
        [ForeignKey(nameof(CreatedById))]
        public virtual PersonEntity Creator { get; set; } = default!; // Inverse of PersonEntity.CreatedRecipes

        [Required]
        public bool IsCurated { get; set; } = false;

        public long? CuratedById { get; set; }
        [ForeignKey(nameof(CuratedById))]
        public virtual PersonEntity? Curator { get; set; } // Inverse of PersonEntity.CuratedRecipes

        [Column(TypeName = "date")]
        public DateOnly? CuratedDate { get; set; }

        public virtual ICollection<RecipeIngredientEntity>? Ingredients { get; set; }
        public virtual ICollection<RecipeStepEntity>? Steps { get; set; }
        public virtual ICollection<ReferenceEntity>? RecipeTypes { get; set; }
        public virtual ICollection<Plan.MealEntity>? Meals { get; set; }
    }
}
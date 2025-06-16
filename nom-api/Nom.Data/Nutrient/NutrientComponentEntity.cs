using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Nom.Data.Nutrient
{
    /// <summary>
    /// Represents a component relationship between nutrients (e.g., "Protein" is composed of "Amino Acids").
    /// Maps to the 'Nutrient.nutrient_component' table.
    /// </summary>
    [Table("NutrientComponent", Schema = "nutrient")] // Adjusted: Table name capitalized, schema lowercase
    public class NutrientComponentEntity : BaseEntity
    {
        /// <summary>
        /// Foreign key to the NutrientEntity that represents the larger, parent nutrient (e.g., "Protein").
        /// Corresponds to BIGINT NOT NULL.
        /// </summary>
        [Required]
        public long MacroNutrientId { get; set; }

        /// <summary>
        /// Navigation property to the parent MacroNutrient.
        /// [InverseProperty] specifies the navigation property on the other side of the relationship (NutrientEntity).
        /// </summary>
        [ForeignKey(nameof(MacroNutrientId))]
        [InverseProperty(nameof(NutrientEntity.MacroComponents))] // Points to the ICollection on NutrientEntity
        public virtual NutrientEntity MacroNutrient { get; set; } = default!;


        /// <summary>
        /// Foreign key to the NutrientEntity that represents the smaller, component nutrient (e.g., "Leucine").
        /// Corresponds to BIGINT NOT NULL.
        /// </summary>
        [Required]
        public long MicroNutrientId { get; set; }

        /// <summary>
        /// Navigation property to the child MicroNutrient.
        /// [InverseProperty] specifies the navigation property on the other side of the relationship (NutrientEntity).
        /// </summary>
        [ForeignKey(nameof(MicroNutrientId))]
        [InverseProperty(nameof(NutrientEntity.MicroComponents))] // Points to the ICollection on NutrientEntity
        public virtual NutrientEntity MicroNutrient { get; set; } = default!;
    }
}
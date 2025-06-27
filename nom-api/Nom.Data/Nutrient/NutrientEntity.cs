// Nom.Data/Nutrient/NutrientEntity.cs
using Nom.Data.Reference; // For MeasurementTypeViewEntity
using Nom.Data.Recipe; // For IngredientNutrientEntity
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema; // For ForeignKey, InverseProperty

namespace Nom.Data.Nutrient
{
    /// <summary>
    /// Represents a single nutrient (e.g., Protein, Vitamin C, Sodium).
    /// </summary>
    [Table("Nutrient", Schema = "nutrient")]
    public class NutrientEntity : BaseEntity, IAuditableEntity // Assuming BaseEntity provides Id, CreatedDate, CreatedByPersonId, LastModifiedDate, LastModifiedByPersonId
    {
        /// <summary>
        /// The common name of the nutrient (e.g., "Protein", "Vitamin C", "Total Carbohydrates").
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// A brief description of the nutrient's role or characteristics.
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// The default unit of measurement for this nutrient (e.g., grams, milligrams, micrograms).
        /// This links to a ReferenceEntity within the MeasurementType group.
        /// </summary>
        public long DefaultMeasurementTypeId { get; set; }

        /// <summary>
        /// Navigation property for the default measurement type.
        /// </summary>
        [ForeignKey(nameof(DefaultMeasurementTypeId))]
        public ReferenceEntity? DefaultMeasurementType { get; set; } // ReferenceEntity is in Nom.Data.Reference

        /// <summary>
        /// Navigation property for ingredients that contain this nutrient.
        /// </summary>
        public ICollection<IngredientNutrientEntity> IngredientNutrients { get; set; } = new List<IngredientNutrientEntity>();

        /// <summary>
        /// Navigation property for nutrient guidelines associated with this nutrient.
        /// </summary>
        public ICollection<NutrientGuidelineEntity> NutrientGuidelines { get; set; } = new List<NutrientGuidelineEntity>();


        // --- Self-referencing properties for Nutrient Components ---
        /// <summary>
        /// Foreign key to the parent nutrient if this nutrient is a component of another.
        /// E.g., Saturated Fat's ParentNutrientId would point to Total Fat.
        /// </summary>
        public long? ParentNutrientId { get; set; }

        /// <summary>
        /// Navigation property to the parent nutrient if this nutrient is a component of another.
        /// </summary>
        [ForeignKey(nameof(ParentNutrientId))]
        [InverseProperty(nameof(ChildNutrients))] // Point back to the collection of children on the parent
        public NutrientEntity? ParentNutrient { get; set; }

        /// <summary>
        /// Collection of child nutrients that are components of this nutrient.
        /// E.g., Total Fat would have Saturated Fat, Monounsaturated Fat, etc., in this collection.
        /// </summary>
        [InverseProperty(nameof(ParentNutrient))] // Point to the ParentNutrient property on the child
        public ICollection<NutrientEntity> ChildNutrients { get; set; } = new List<NutrientEntity>();
    }
}

// File: Nom.Data/Recipe/IngredientEntity.cs

using System.Collections.Generic;
using Nom.Data.Nutrient;
using Nom.Data.Person;
using Nom.Data.Reference;

namespace Nom.Data.Recipe
{
    public class IngredientEntity : BaseEntity
    {
        public string Name { get; set; } = string.Empty;

        public string? Description { get; set; }

        // Mealie-specific fields
        public string? PluralName { get; set; }

        public string? FdcId { get; set; }

        public string FdcDataType { get; set; } = string.Empty;

        // Normalized search fields (from Mealie)
        public string? NameNormalized { get; set; }

        public string? PluralNameNormalized { get; set; }

        // Curation and ownership
        public long CurationStatusId { get; set; }
        public virtual ReferenceEntity? CurationStatus { get; set; }

        public long? AuthorId { get; set; }
        public virtual PersonEntity? Author { get; set; }

        // Label association (from Mealie)
        public long? LabelId { get; set; }
        public virtual ReferenceEntity? Label { get; set; }

        // Legacy field (from Mealie)
        public bool? OnHand { get; set; } = false;

        // Navigation properties
        public virtual ICollection<IngredientNutrientEntity> IngredientNutrients { get; set; } = new List<IngredientNutrientEntity>();

        /// <summary>Sub-ingredients of this (composite) ingredient, label-ordered.</summary>
        public virtual ICollection<IngredientComponentEntity> Components { get; set; } = new List<IngredientComponentEntity>();

        /// <summary>Curated substitutions usable wherever this ingredient appears.</summary>
        public virtual ICollection<IngredientSubstitutionEntity> Substitutions { get; set; } = new List<IngredientSubstitutionEntity>();
        public virtual ICollection<IngredientAliasEntity> Aliases { get; set; } = new List<IngredientAliasEntity>();

        // New navigation properties (from Mealie)
        public virtual ICollection<RecipeIngredientEntity> RecipeIngredients { get; set; } = new List<RecipeIngredientEntity>();
        public virtual ICollection<IngredientExtrasEntity> Extras { get; set; } = new List<IngredientExtrasEntity>();
    }
}

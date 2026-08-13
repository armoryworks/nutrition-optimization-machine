// File: nom-api/Nom.Data/Plan/RestrictionCriterionEntity.cs

using Nom.Data.Recipe;
using Nom.Data.Reference;
using Nom.Data.Nutrient;

namespace Nom.Data.Plan
{
    /// <summary>
    /// A filter criterion attached to a restriction CATEGORY (the reference row,
    /// e.g. "Gout"): which ingredients (exact id or ILIKE name pattern) or
    /// nutrient limits the category flags. Person restrictions that point at the
    /// category inherit its criteria during diet evaluation; admins curate them.
    /// </summary>
    public class RestrictionCriterionEntity : BaseEntity
    {
        /// <summary>The restriction category (reference row) this criterion belongs to.</summary>
        public long RestrictionTypeId { get; set; }
        public virtual ReferenceEntity? RestrictionType { get; set; }

        /// <summary>Exact ingredient match, when the criterion targets one ingredient.</summary>
        public long? IngredientId { get; set; }
        public virtual IngredientEntity? Ingredient { get; set; }

        /// <summary>
        /// ILIKE pattern matched against ingredient names (e.g. '%anchov%') —
        /// survives ingredient imports the exact id can't anticipate.
        /// </summary>
        public string? IngredientPattern { get; set; }

        /// <summary>Nutrient-level criterion (e.g. sodium), with an optional per-serving cap.</summary>
        public long? NutrientId { get; set; }
        public virtual NutrientEntity? Nutrient { get; set; }

        /// <summary>Cap per serving in the nutrient's unit; null = flag any presence.</summary>
        public decimal? MaxAmountPerServing { get; set; }

        /// <summary>1 = mild preference … 5 = absolute (allergy-grade). Mirrors Restriction.Severity.</summary>
        public int Severity { get; set; } = 3;

        /// <summary>Shown to users on a hit ("high oxalate", "purine-rich").</summary>
        public string? Notes { get; set; }
    }
}

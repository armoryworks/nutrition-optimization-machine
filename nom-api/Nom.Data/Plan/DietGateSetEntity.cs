using System;

namespace Nom.Data.Plan
{
    /// <summary>
    /// One restriction set of a diet gate build: a restriction type's nutrient, word and name-pattern criteria, all of
    /// them (<see cref="MinimumSeverity"/> 0, what a household's plans read) or those of severity 4 and 5 (what a person's
    /// search reads) when that list differs. Its facts and flags are complete once <see cref="State"/> is
    /// <c>complete</c>; until then a request that needs it reads the type's <c>superseded</c> set, or holds every recipe
    /// when the type has none.
    /// </summary>
    public class DietGateSetEntity
    {
        public long Id { get; set; }

        public long BuildId { get; set; }

        /// <summary>The SHA-256 (hex) of the criteria as the gate reads them, without the rule version: a request finds its set by it.</summary>
        public string CriteriaKey { get; set; } = string.Empty;

        public long RestrictionTypeId { get; set; }

        /// <summary>The lowest criterion severity the set reads: 0 or 4.</summary>
        public int MinimumSeverity { get; set; }

        /// <summary>Whether a criterion of the set is of severity 4 or 5: a recipe or food the set cannot decide is then held.</summary>
        public bool Severe { get; set; }

        /// <summary>The criteria as the gate reads them, for operators.</summary>
        public string Criteria { get; set; } = string.Empty;

        /// <summary><c>building</c>, <c>complete</c>, or <c>superseded</c> (the type's criteria changed; kept for requests until the new set is complete).</summary>
        public string State { get; set; } = string.Empty;

        public DateTime? BuiltAt { get; set; }

        /// <summary>Seconds the set's foods took to decide when it was built.</summary>
        public double? FoodSeconds { get; set; }

        /// <summary>Seconds the set's recipes took to decide when it was built.</summary>
        public double? RecipeSeconds { get; set; }

        /// <summary>How many foods the set's criteria count.</summary>
        public int Foods { get; set; }

        /// <summary>How many recipes the set flags as containing what it restricts.</summary>
        public int RecipesContaining { get; set; }

        /// <summary>How many recipes the set could not decide (no ingredient line, or a food not yet decided).</summary>
        public int RecipesUnknown { get; set; }
    }
}

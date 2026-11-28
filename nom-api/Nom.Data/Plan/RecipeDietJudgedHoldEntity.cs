using System;

namespace Nom.Data.Plan
{
    /// <summary>
    /// A recipe that people judged to hold a food a restriction type keeps out, while the rules showed it (a judged miss of a
    /// diet draw or a staging checkpoint): the recipe, the restriction type, a short word for the food, who judged it and
    /// when, and the evidence file the judgement is kept in. The stored diet gate flags the recipe for every set of the type
    /// as <see cref="DietFlagReason.Judged"/> unless its rules already flag it, so a miss once found stays held whatever the
    /// rules do. A row only holds; nothing here clears a recipe. Rows are written by the journaled load of
    /// ops/diet-judged-holds.py and taken back only by the undo of that load; a row never holds a nutrition number.
    /// </summary>
    public class RecipeDietJudgedHoldEntity
    {
        public long Id { get; set; }

        public long RecipeId { get; set; }

        public long RestrictionTypeId { get; set; }

        /// <summary>A short word for the food judged hidden in the recipe, e.g. <c>butter</c> or <c>dried figs</c>; never recipe text.</summary>
        public string Food { get; set; } = string.Empty;

        public string JudgedBy { get; set; } = string.Empty;

        public DateTime JudgedAt { get; set; }

        /// <summary>The name of the evidence file that holds the judgement.</summary>
        public string Evidence { get; set; } = string.Empty;

        /// <summary>The stamp of the load that wrote the row, the key its undo takes it back by.</summary>
        public DateTime LoadedAt { get; set; }

        public long LoadedByPersonId { get; set; }
    }
}

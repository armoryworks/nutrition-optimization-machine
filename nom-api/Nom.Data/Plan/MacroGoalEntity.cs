using Nom.Data.Person;

namespace Nom.Data.Plan
{
    /// <summary>
    /// Daily macronutrient targets owned by exactly one person OR one household
    /// (enforced by a check constraint). A person's goal overrides their
    /// household's when resolving effective targets; the household goal is the
    /// default for members without one and drives household-scoped meal
    /// selection (shuffle scoring). Null targets mean "no goal for this macro".
    /// Maps to the 'plan.MacroGoal' table.
    /// </summary>
    public class MacroGoalEntity : BaseEntity
    {
        public long? PersonId { get; set; }
        public virtual PersonEntity? Person { get; set; }

        public long? HouseholdId { get; set; }
        public virtual HouseholdEntity? Household { get; set; }

        /// <summary>Daily energy target in kilocalories.</summary>
        public decimal? CaloriesTarget { get; set; }

        /// <summary>Daily protein target in grams.</summary>
        public decimal? ProteinGramsTarget { get; set; }

        /// <summary>Daily carbohydrate target in grams.</summary>
        public decimal? CarbGramsTarget { get; set; }

        /// <summary>Daily fat target in grams.</summary>
        public decimal? FatGramsTarget { get; set; }
    }
}

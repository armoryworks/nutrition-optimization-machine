using Nom.Data.Person;

namespace Nom.Data.Plan
{
    /// <summary>
    /// A grocery-spend budget owned by exactly one person OR one household
    /// (check-constrained, same shape as MacroGoal). A person's budget overrides
    /// their household's when resolving the effective budget for shopping.
    /// Maps to the 'plan.Budget' table.
    /// </summary>
    public class BudgetEntity : BaseEntity
    {
        public long? PersonId { get; set; }
        public virtual PersonEntity? Person { get; set; }

        public long? HouseholdId { get; set; }
        public virtual HouseholdEntity? Household { get; set; }

        /// <summary>Budget amount for one period.</summary>
        public decimal Amount { get; set; }

        /// <summary>ISO 4217 currency code, e.g. "USD".</summary>
        public string Currency { get; set; } = "USD";

        /// <summary>Period the amount covers: "weekly" or "monthly".</summary>
        public string Period { get; set; } = "weekly";
    }
}

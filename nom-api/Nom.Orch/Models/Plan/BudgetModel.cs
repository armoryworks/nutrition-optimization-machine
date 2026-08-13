namespace Nom.Orch.Models.Plan
{
    /// <summary>A grocery-spend budget for a person or household.</summary>
    public class BudgetModel
    {
        public decimal Amount { get; set; }
        public string Currency { get; set; } = "USD";
        /// <summary>"weekly" or "monthly".</summary>
        public string Period { get; set; } = "weekly";
    }

    /// <summary>The budget that effectively applies to a person: own, else household, else none.</summary>
    public class EffectiveBudgetModel : BudgetModel
    {
        public bool HasBudget { get; set; }
        /// <summary>"person", "household", or "none".</summary>
        public string Source { get; set; } = "none";
    }
}

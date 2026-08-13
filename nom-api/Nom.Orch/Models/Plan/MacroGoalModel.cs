namespace Nom.Orch.Models.Plan
{
    /// <summary>
    /// Daily macronutrient targets for a person or a household. Null means no
    /// target is set for that macro.
    /// </summary>
    public class MacroGoalModel
    {
        public decimal? CaloriesTarget { get; set; }
        public decimal? ProteinGramsTarget { get; set; }
        public decimal? CarbGramsTarget { get; set; }
        public decimal? FatGramsTarget { get; set; }

        public bool HasAnyTarget =>
            CaloriesTarget.HasValue || ProteinGramsTarget.HasValue
            || CarbGramsTarget.HasValue || FatGramsTarget.HasValue;
    }
}

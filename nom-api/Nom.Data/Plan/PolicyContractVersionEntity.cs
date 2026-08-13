namespace Nom.Data.Plan
{
    /// <summary>
    /// Single-row version marker for the policy-table integration surface
    /// (household-policies design doc §6). Bumped on breaking shape changes to
    /// the policy tables; external management tools refuse to run against an
    /// unknown version. Seeded with Id=1, Version=1.
    /// </summary>
    public class PolicyContractVersionEntity : BaseEntity
    {
        public int Version { get; set; }
    }
}

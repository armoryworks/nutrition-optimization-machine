namespace Nom.Data.Plan
{
    /// <summary>
    /// Represents a grouping category for households.
    /// Maps to the 'plan.HouseholdGroup' table.
    /// </summary>
    public class HouseholdGroupEntity : BaseEntity
    {
        public string Name { get; set; } = string.Empty;

        public string? Description { get; set; }

        public string? Slug { get; set; }
    }
}

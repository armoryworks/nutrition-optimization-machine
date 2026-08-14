namespace Nom.Orch.Models.MealPlan
{
    /// <summary>A household food-group requirement (read model).</summary>
    public class FoodGroupRuleModel
    {
        public long Id { get; set; }
        public long HouseholdId { get; set; }
        public long FoodGroupId { get; set; }
        public string? FoodGroupName { get; set; }
        public decimal MinServings { get; set; }
        /// <summary>"PerDay" or "PerMeal".</summary>
        public string Timeframe { get; set; } = "PerDay";
        public long? MealTypeId { get; set; }
        public string? MealTypeName { get; set; }
        public bool IsActive { get; set; }
    }

    /// <summary>Create/update payload for a household food-group requirement.</summary>
    public class FoodGroupRuleUpsertModel
    {
        public long HouseholdId { get; set; }
        public long FoodGroupId { get; set; }
        public decimal MinServings { get; set; }
        /// <summary>"PerDay" or "PerMeal". Defaults to PerDay.</summary>
        public string Timeframe { get; set; } = "PerDay";
        public long? MealTypeId { get; set; }
        public bool IsActive { get; set; } = true;
    }

    /// <summary>A selectable nutritional food group (from the FoodGroupType reference group).</summary>
    public class FoodGroupModel
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
    }
}

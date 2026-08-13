// File: Nom.Orch/Models/Household/HouseholdResponseModel.cs

namespace Nom.Orch.Models.Household
{
    public class HouseholdResponseModel
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public long HouseholdGroupId { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime? ModifiedDate { get; set; }

        /// <summary>
        /// Opaque marker of the external management tool this household is
        /// enrolled with (e.g. "brigade:123"), or null when self-managed.
        /// </summary>
        public string? ManagedBy { get; set; }

        // Members
        public List<HouseholdMemberResponseModel>? Members { get; set; }

        // Statistics
        public int MemberCount { get; set; }
        public int RecipeCount { get; set; }
        public int PlanCount { get; set; }
        public int ShoppingListCount { get; set; }
    }
} 
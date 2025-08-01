// File: Nom.Orch/Models/Household/HouseholdUpdateModel.cs

namespace Nom.Orch.Models.Household
{
    public class HouseholdUpdateModel
    {
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public long? GroupId { get; set; }
    }
} 
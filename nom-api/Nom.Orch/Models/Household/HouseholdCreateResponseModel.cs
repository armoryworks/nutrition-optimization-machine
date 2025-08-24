// File: Nom.Orch/Models/Household/HouseholdCreateResponseModel.cs

namespace Nom.Orch.Models.Household
{
    public class HouseholdCreateResponseModel
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public long GroupId { get; set; }
        public DateTime CreatedDate { get; set; }
    }
} 
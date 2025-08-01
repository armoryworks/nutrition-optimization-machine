namespace Nom.Orch.Models.Shopping
{
    public class ShoppingListCategoryResponseModel
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public long HouseholdId { get; set; }
        public string HouseholdName { get; set; } = string.Empty;
        public int SortOrder { get; set; }
        public string? Color { get; set; }
        public int ItemCount { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime? LastModifiedDate { get; set; }
    }
} 
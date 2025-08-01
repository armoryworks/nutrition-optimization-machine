// File: Nom.Orch/Models/Shopping/ShoppingListResponseModel.cs

namespace Nom.Orch.Models.Shopping
{
    public class ShoppingListResponseModel
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public long AuthorId { get; set; }
        public long? HouseholdId { get; set; }
        public long? GroupId { get; set; }
        public int ItemCount { get; set; }
        public int CompletedItemCount { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime? ModifiedDate { get; set; }
    }
} 
// File: Nom.Orch/Models/Shopping/ShoppingListCreateResponseModel.cs

namespace Nom.Orch.Models.Shopping
{
    public class ShoppingListCreateResponseModel
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public long AuthorId { get; set; }
        public long? HouseholdId { get; set; }
        public long? GroupId { get; set; }
        public DateTime CreatedDate { get; set; }
    }
} 
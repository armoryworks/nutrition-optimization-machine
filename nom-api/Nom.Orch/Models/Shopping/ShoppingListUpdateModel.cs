// File: Nom.Orch/Models/Shopping/ShoppingListUpdateModel.cs

namespace Nom.Orch.Models.Shopping
{
    public class ShoppingListUpdateModel
    {
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public long? HouseholdId { get; set; }
        public long? ShoppingListGroupId { get; set; }
    }
} 
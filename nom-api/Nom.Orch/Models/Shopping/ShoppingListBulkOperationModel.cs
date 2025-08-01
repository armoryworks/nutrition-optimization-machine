namespace Nom.Orch.Models.Shopping
{
    public class ShoppingListBulkOperationModel
    {
        public List<long> ItemIds { get; set; } = new();
        public string Operation { get; set; } = string.Empty; // "complete", "delete", "move"
        public long? TargetCategoryId { get; set; }
    }
} 
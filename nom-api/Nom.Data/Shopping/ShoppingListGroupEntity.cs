namespace Nom.Data.Shopping
{
    /// <summary>
    /// Represents a grouping category for shopping lists.
    /// Maps to the 'shopping.ShoppingListGroup' table.
    /// </summary>
    public class ShoppingListGroupEntity : BaseEntity
    {
        public string Name { get; set; } = string.Empty;

        public string? Description { get; set; }

        public string? Slug { get; set; }
    }
}

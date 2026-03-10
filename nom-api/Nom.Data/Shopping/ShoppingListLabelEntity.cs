// File: Nom.Data/Shopping/ShoppingListLabelEntity.cs

using Nom.Data.Audit;
using Nom.Data.Reference;

namespace Nom.Data.Shopping
{
    public class ShoppingListLabelEntity : BaseEntity
    {
        public long ShoppingListId { get; set; }
        public virtual ShoppingListEntity? ShoppingList { get; set; }

        public long LabelId { get; set; }
        public virtual ReferenceEntity? Label { get; set; }
    }
}

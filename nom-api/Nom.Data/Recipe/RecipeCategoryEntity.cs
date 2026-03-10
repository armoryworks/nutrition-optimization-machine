// File: Nom.Data/Recipe/RecipeCategoryEntity.cs

using Nom.Data.Audit;
using Nom.Data.Reference;

namespace Nom.Data.Recipe
{
    public class RecipeCategoryEntity : BaseEntity
    {
        public long RecipeId { get; set; }
        public virtual RecipeEntity? Recipe { get; set; }

        public long CategoryId { get; set; }
        public virtual ReferenceEntity? Category { get; set; }
    }
}

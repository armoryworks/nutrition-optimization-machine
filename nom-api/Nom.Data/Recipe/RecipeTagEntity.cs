// File: Nom.Data/Recipe/RecipeTagEntity.cs

using Nom.Data.Audit;
using Nom.Data.Reference;

namespace Nom.Data.Recipe
{
    public class RecipeTagEntity : BaseEntity
    {
        public long RecipeId { get; set; }
        public virtual RecipeEntity? Recipe { get; set; }

        public long TagId { get; set; }
        public virtual ReferenceEntity? Tag { get; set; }
    }
}

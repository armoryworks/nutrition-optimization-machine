// File: nom-api/Nom.Data/Recipe/RecipeVariationEntity.cs

using Nom.Data.Person;

namespace Nom.Data.Recipe
{
    /// <summary>
    /// A person's saved default variation of a recipe: the set of ingredient
    /// substitutions they always apply. One per (recipe, person).
    /// </summary>
    public class RecipeVariationEntity : BaseEntity
    {
        public long RecipeId { get; set; }
        public virtual RecipeEntity? Recipe { get; set; }

        public long PersonId { get; set; }
        public virtual PersonEntity? Person { get; set; }

        public virtual ICollection<RecipeVariationItemEntity> Items { get; set; } = new List<RecipeVariationItemEntity>();
    }
}

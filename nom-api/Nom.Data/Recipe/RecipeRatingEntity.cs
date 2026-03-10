// File: Nom.Data/Recipe/RecipeRatingEntity.cs

using System;
using Nom.Data.Audit;
using Nom.Data.Person;

namespace Nom.Data.Recipe
{
    public class RecipeRatingEntity : BaseEntity
    {
        public long RecipeId { get; set; }
        public virtual RecipeEntity? Recipe { get; set; }

        public long RaterId { get; set; }
        public virtual PersonEntity? Rater { get; set; }

        public decimal Rating { get; set; }

        public DateTime? DateRated { get; set; }
    }
}

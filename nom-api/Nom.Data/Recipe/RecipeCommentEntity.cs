// File: Nom.Data/Recipe/RecipeCommentEntity.cs

using System;
using Nom.Data.Audit;
using Nom.Data.Person;

namespace Nom.Data.Recipe
{
    public class RecipeCommentEntity : BaseEntity
    {
        public long RecipeId { get; set; }
        public virtual RecipeEntity? Recipe { get; set; }

        public long AuthorId { get; set; }
        public virtual PersonEntity? Author { get; set; }

        public string Comment { get; set; } = string.Empty;
    }
}

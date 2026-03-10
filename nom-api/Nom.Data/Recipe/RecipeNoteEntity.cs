// File: Nom.Data/Recipe/RecipeNoteEntity.cs

using System;
using Nom.Data.Audit;
using Nom.Data.Person;

namespace Nom.Data.Recipe
{
    public class RecipeNoteEntity : BaseEntity
    {
        public long RecipeId { get; set; }
        public virtual RecipeEntity? Recipe { get; set; }

        public long AuthorId { get; set; }
        public virtual PersonEntity? Author { get; set; }

        public string Note { get; set; } = string.Empty;

        public bool IsPublic { get; set; } = false;

        public string? Title { get; set; }
    }
}

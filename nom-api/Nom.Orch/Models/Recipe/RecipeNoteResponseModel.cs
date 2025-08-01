using System;

namespace Nom.Orch.Models.Recipe
{
    public class RecipeNoteResponseModel
    {
        public long Id { get; set; }
        public long RecipeId { get; set; }
        public string RecipeName { get; set; } = string.Empty;
        public long AuthorId { get; set; }
        public string AuthorName { get; set; } = string.Empty;
        public string? Title { get; set; }
        public string Note { get; set; } = string.Empty;
        public bool IsPublic { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime? LastModifiedDate { get; set; }
    }
} 
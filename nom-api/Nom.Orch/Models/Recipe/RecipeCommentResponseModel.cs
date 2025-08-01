using System;

namespace Nom.Orch.Models.Recipe
{
    public class RecipeCommentResponseModel
    {
        public long Id { get; set; }
        public long RecipeId { get; set; }
        public long AuthorId { get; set; }
        public string AuthorName { get; set; } = string.Empty;
        public string Comment { get; set; } = string.Empty;
        public DateTime CreatedDate { get; set; }
        public DateTime? LastModifiedDate { get; set; }
    }
} 
using System;

namespace Nom.Orch.Models.Recipe
{
    public class RecipeTimelineEventResponseModel
    {
        public long Id { get; set; }
        public long RecipeId { get; set; }
        public string RecipeName { get; set; } = string.Empty;
        public long EventTypeId { get; set; }
        public string EventTypeName { get; set; } = string.Empty;
        public string EventTitle { get; set; } = string.Empty;
        public string? EventDescription { get; set; }
        public DateTime? EventDate { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime? LastModifiedDate { get; set; }
    }
} 
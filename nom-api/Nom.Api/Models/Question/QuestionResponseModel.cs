// Nom.Api/Models/Question/QuestionResponseModel.cs
using System.Collections.Generic;

namespace Nom.Api.Models.Question
{
    /// <summary>
    /// Represents a question as returned by the API to the frontend.
    /// </summary>
    public class QuestionResponseModel
    {
        public long Id { get; set; }
        public required string Text { get; set; }
        public string? Hint { get; set; }
        public long QuestionCategoryId { get; set; } // Can be mapped to a CategoryName if needed later
        public required string AnswerType { get; set; } // Will be the string name of the enum (e.g., "Yes/No", "TextInput")
        public int DisplayOrder { get; set; }
        public bool IsActive { get; set; }
        public bool IsRequiredForPlanCreation { get; set; }
        public string? DefaultAnswer { get; set; } // For text inputs or single default choice
        public string? ValidationRegex { get; set; } // Regex for text input validation
        public List<string>? Options { get; set; } // For multi-select/single-select choices
    }
}
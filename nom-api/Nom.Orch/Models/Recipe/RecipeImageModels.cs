using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Nom.Orch.Models.Recipe
{
    /// <summary>A published recipe with no image, in the shape the image finder reads.</summary>
    public class RecipeImageWorkItemModel
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? DishGroup { get; set; }
        public List<string> Ingredients { get; set; } = new();
        public List<string> Steps { get; set; } = new();
        public int RatingCount { get; set; }

        /// <summary>"source:id" keys an admin already rejected for this recipe — never proposed again.</summary>
        public List<string> RejectedCandidates { get; set; } = new();
    }

    public class RecipeImageSubmissionModel
    {
        public long RecipeId { get; set; }
        public string? Batch { get; set; }
        public List<RecipeImageCandidateSubmissionModel> Candidates { get; set; } = new();
    }

    public class RecipeImageCandidateSubmissionModel
    {
        public string SourceKey { get; set; } = string.Empty;
        public string SourceName { get; set; } = string.Empty;
        public string SourceId { get; set; } = string.Empty;
        public string? Title { get; set; }
        public string? Author { get; set; }
        public string? AuthorUrl { get; set; }
        public string License { get; set; } = string.Empty;
        public string LicenseCode { get; set; } = string.Empty;
        public string? LicenseUrl { get; set; }
        public string? LandingUrl { get; set; }
        public string? ImageUrl { get; set; }
        public bool Hotlink { get; set; }
        public string? DownloadLocation { get; set; }
        public decimal? Score { get; set; }
        public JsonElement? Checks { get; set; }

        /// <summary>"attached" when every check passed (applied immediately), otherwise "pending".</summary>
        public string Status { get; set; } = string.Empty;

        /// <summary>The image itself, for sources that are rehosted. Empty for hotlink-only sources.</summary>
        public string? ImageBase64 { get; set; }
        public string? ContentType { get; set; }
    }

    public class RecipeImageSubmissionResultModel
    {
        public List<RecipeImageCandidateOutcomeModel> Outcomes { get; set; } = new();
    }

    public class RecipeImageCandidateOutcomeModel
    {
        public string SourceKey { get; set; } = string.Empty;
        public string SourceId { get; set; } = string.Empty;
        public long? CandidateId { get; set; }

        /// <summary>attached | pending | duplicate | rejected</summary>
        public string Outcome { get; set; } = string.Empty;
        public string? Message { get; set; }
    }

    public class RecipeImageCandidateModel
    {
        public long Id { get; set; }
        public long RecipeId { get; set; }
        public string RecipeName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string SourceKey { get; set; } = string.Empty;
        public string SourceName { get; set; } = string.Empty;
        public string? Title { get; set; }
        public string? Author { get; set; }
        public string? AuthorUrl { get; set; }
        public string License { get; set; } = string.Empty;
        public string? LicenseUrl { get; set; }
        public string? LandingUrl { get; set; }
        public bool Hotlink { get; set; }

        /// <summary>Where the admin UI can show the photo: the remote URL for hotlinked sources, else null (fetch the preview endpoint).</summary>
        public string? PreviewUrl { get; set; }
        public decimal? Score { get; set; }
        public string? ChecksJson { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime? ReviewedAt { get; set; }
    }

    public class RecipeImageCreditModel
    {
        public string? SourceName { get; set; }
        public string? SourceUrl { get; set; }
        public string? Author { get; set; }
        public string? AuthorUrl { get; set; }
        public string? License { get; set; }
        public string? LicenseUrl { get; set; }

        /// <summary>True for no-derivatives licences: the image must be shown uncropped.</summary>
        public bool NoDerivatives { get; set; }
    }

    public class RecipeImageDownloadTrackingModel
    {
        public long Id { get; set; }
        public string DownloadLocation { get; set; } = string.Empty;
    }
}

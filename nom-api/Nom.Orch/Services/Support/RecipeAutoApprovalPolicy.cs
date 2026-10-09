using System;
using Nom.Data.Recipe;

namespace Nom.Orch.Services.Support
{
    public sealed record AutoApprovalDecision(bool Eligible, string? Rule, string? Reason);

    /// <summary>
    /// When a recipe may be approved without a curator. R1: a public-domain recipe that passed
    /// vetting and publishes no source image. R2: a scraped recipe of unknown license whose prose
    /// has been rewritten and which carries an image of our own. Both also need a Public,
    /// never-reviewed NonCurated recipe and must pass <see cref="RecipeApprovalGate"/>.
    /// </summary>
    public static class RecipeAutoApprovalPolicy
    {
        public const string PublicDomainRule = "R1 public domain";
        public const string RewrittenScrapeRule = "R2 rewritten scrape with own image";

        public static AutoApprovalDecision Evaluate(RecipeEntity recipe)
        {
            if (recipe.IsDeleted) return No("deleted");
            if (recipe.CurationStatusId != (long)CurationStatusEnum.NonCurated) return No("not NonCurated");
            if (recipe.Visibility != RecipeVisibilityEnum.Public) return No("not Public");
            if (recipe.DateCurationCompleted != null) return No("a curator has already reviewed it");
            if (!string.IsNullOrWhiteSpace(recipe.VettingIssues)) return No("has vetting issues");
            if (recipe.ContainsSourceProse) return No("contains source prose");

            string rule;
            if (recipe.LicenseStatus == RecipeLicenseStatus.PublicDomain)
            {
                rule = PublicDomainRule;
            }
            else if (recipe.LicenseStatus == RecipeLicenseStatus.Unknown && recipe.ScrapedAtUtc != null)
            {
                if (string.IsNullOrWhiteSpace(recipe.Image)) return No("no image of our own");
                rule = RewrittenScrapeRule;
            }
            else
            {
                return No($"license '{recipe.LicenseStatus ?? "none"}' is not auto-approvable");
            }

            if (UsesSourceImage(recipe)) return No("its image is the source's image");
            if (RecipeApprovalGate.RefusalReason(recipe) is { } refusal) return No(refusal);

            return new AutoApprovalDecision(true, rule, null);
        }

        public static bool UsesSourceImage(RecipeEntity recipe)
        {
            if (string.IsNullOrWhiteSpace(recipe.Image)) return false;

            var image = recipe.Image.Trim();
            if (!string.IsNullOrWhiteSpace(recipe.SourceImageUrl)
                && string.Equals(image, recipe.SourceImageUrl.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return Uri.TryCreate(image, UriKind.Absolute, out var imageUri)
                && (SameHost(imageUri, recipe.SourceImageUrl) || SameHost(imageUri, recipe.SourceUrl));
        }

        private static bool SameHost(Uri image, string? other) =>
            Uri.TryCreate(other?.Trim(), UriKind.Absolute, out var otherUri)
            && string.Equals(BareHost(image), BareHost(otherUri), StringComparison.OrdinalIgnoreCase);

        private static string BareHost(Uri uri) =>
            uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host;

        private static AutoApprovalDecision No(string reason) => new(false, null, reason);
    }
}

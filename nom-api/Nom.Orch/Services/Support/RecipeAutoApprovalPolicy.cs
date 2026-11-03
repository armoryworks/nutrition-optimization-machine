using System;
using Nom.Data.Recipe;

namespace Nom.Orch.Services.Support
{
    /// <summary>A refusal's <see cref="Key"/> names its rule without the recipe's particulars, so sweeps can be tallied.</summary>
    public sealed record AutoApprovalDecision(bool Eligible, string? Rule, string? Reason, string? Key = null);

    /// <summary>
    /// Operator choices in the policy. <see cref="MaxUnquantifiedLines"/> is how many real ingredient
    /// lines may lack an amount (0: none; the label then marks them as not measured).
    /// <see cref="RequireOwnImageForUnknownLicense"/> keeps rule R2 waiting for a photo of our own.
    /// </summary>
    public sealed record AutoApprovalOptions(int MaxUnquantifiedLines = 0, bool RequireOwnImageForUnknownLicense = true)
    {
        public static readonly AutoApprovalOptions Default = new();
    }

    /// <summary>
    /// When a recipe may be approved without a curator. R1: a public-domain recipe that passed
    /// vetting and publishes no source image. R2: a scraped recipe of unknown license whose prose
    /// has been rewritten and which carries an image of our own (unless the operator waives that).
    /// Both also need a Public, never-reviewed NonCurated recipe, a quantity on every line that isn't a
    /// seasoning, a garnish or a line classified as something other than an ingredient (up to the
    /// operator's allowance), and must pass <see cref="RecipeApprovalGate"/>.
    /// </summary>
    public static class RecipeAutoApprovalPolicy
    {
        public const string PublicDomainRule = "R1 public domain";
        public const string RewrittenScrapeRule = "R2 rewritten scrape with own image";
        public const string UnquantifiedKey = "unquantified ingredient lines";
        public const string UncuratedKey = "uncurated ingredients";
        public const string NoImageKey = "no image of our own";

        public static AutoApprovalDecision Evaluate(RecipeEntity recipe) => Evaluate(recipe, AutoApprovalOptions.Default);

        public static AutoApprovalDecision Evaluate(RecipeEntity recipe, AutoApprovalOptions options)
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
                if (options.RequireOwnImageForUnknownLicense && string.IsNullOrWhiteSpace(recipe.Image)) return No(NoImageKey);
                rule = RewrittenScrapeRule;
            }
            else
            {
                return No($"license '{recipe.LicenseStatus ?? "none"}' is not auto-approvable", "license not auto-approvable");
            }

            if (UsesSourceImage(recipe)) return No("its image is the source's image");
            var unquantified = recipe.RecipeIngredients?
                .Count(ri => ri.Quantity <= 0 && !RecipeVettingService.IsAcceptablyUnquantified(ri.RawLine, ri.LineKind)) ?? 0;
            if (unquantified > options.MaxUnquantifiedLines) return No($"{unquantified} ingredient line(s) have no quantity", UnquantifiedKey);
            if (RecipeApprovalGate.RefusalReason(recipe) is { } refusal) return No(refusal, UncuratedKey);

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

        private static AutoApprovalDecision No(string reason, string? key = null) => new(false, null, reason, key ?? reason);
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nom.Data;
using Nom.Data.Recipe;
using Nom.Orch.UtilityServices;
using Nom.Orch.Extensions;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Recipe;
using Nom.Orch.UtilityInterfaces;

namespace Nom.Orch.Services
{
    public class RecipeImageService : IRecipeImageService
    {
        public const int MaxImageBytes = 10 * 1024 * 1024;
        public const int MaxCandidatesPerSubmission = 5;
        public const int MaxWidth = 1200;

        public static readonly IReadOnlySet<string> Sources =
            new HashSet<string> { "wikimedia", "openverse", "pexels", "pixabay", "unsplash" };

        /// <summary>Licence families nom-api will store. NC, GFDL and unknown licences are refused here as well as in the finder.</summary>
        public static readonly IReadOnlySet<string> LicenseCodes =
            new HashSet<string> { "cc0", "pd", "by", "by-nd", "by-sa", "pexels", "pixabay", "unsplash" };

        private static readonly IReadOnlySet<string> AttributionRequired = new HashSet<string> { "by", "by-nd", "by-sa", "unsplash" };

        private const string HotlinkSource = "unsplash";
        private const string HotlinkPrefix = "https://images.unsplash.com/";

        private readonly ApplicationDbContext _context;
        private readonly IMediaStorageService _mediaStorage;
        private readonly ILogger<RecipeImageService> _logger;

        public RecipeImageService(ApplicationDbContext context, IMediaStorageService mediaStorage, ILogger<RecipeImageService> logger)
        {
            _context = context;
            _mediaStorage = mediaStorage;
            _logger = logger;
        }

        public async Task<List<RecipeImageWorkItemModel>> GetMissingAsync(int limit)
        {
            limit = Math.Clamp(limit, 1, 500);
            var waiting = _context.RecipeImageCandidates
                .Where(c => c.Status == RecipeImageCandidateStatus.Pending)
                .Select(c => c.RecipeId);

            var recipes = await _context.Recipes
                .AsNoTracking()
                .VisibleTo(_context, null)
                .Where(r => !r.IsDeleted && (r.Image == null || r.Image == "") && !waiting.Contains(r.Id))
                .OrderByDescending(r => r.Ratings!.Count())
                .ThenByDescending(r => r.Rating)
                .ThenBy(r => r.Id)
                .Take(limit)
                .Select(r => new RecipeImageWorkItemModel
                {
                    Id = r.Id,
                    Name = r.Name,
                    Description = r.Description,
                    DishGroup = r.DishGroup != null ? r.DishGroup.Name : null,
                    RatingCount = r.Ratings!.Count(),
                })
                .ToListAsync();

            var ids = recipes.Select(r => r.Id).ToList();
            var ingredients = await _context.RecipeIngredients.AsNoTracking()
                .Where(ri => ids.Contains(ri.RecipeId) && !ri.IsDeleted)
                .OrderBy(ri => ri.Id)
                .Select(ri => new { ri.RecipeId, Name = ri.Ingredient.Name })
                .ToListAsync();
            var steps = await _context.RecipeSteps.AsNoTracking()
                .Where(s => ids.Contains(s.RecipeId) && !s.IsDeleted)
                .OrderBy(s => s.StepNumber)
                .Select(s => new { s.RecipeId, s.Description })
                .ToListAsync();
            var rejected = await _context.RecipeImageCandidates.AsNoTracking()
                .Where(c => ids.Contains(c.RecipeId) && (c.Status == RecipeImageCandidateStatus.Rejected || c.Status == RecipeImageCandidateStatus.Removed))
                .Select(c => new { c.RecipeId, c.SourceKey, c.SourceId })
                .ToListAsync();

            foreach (var recipe in recipes)
            {
                recipe.Ingredients = ingredients.Where(i => i.RecipeId == recipe.Id).Select(i => i.Name).ToList();
                recipe.Steps = steps.Where(s => s.RecipeId == recipe.Id).Select(s => s.Description).ToList();
                recipe.RejectedCandidates = rejected.Where(c => c.RecipeId == recipe.Id).Select(c => $"{c.SourceKey}:{c.SourceId}").ToList();
            }

            return recipes;
        }

        public async Task<RecipeImageSubmissionResultModel?> SubmitAsync(RecipeImageSubmissionModel submission, long? personId)
        {
            var recipe = await _context.Recipes.FirstOrDefaultAsync(r => r.Id == submission.RecipeId && !r.IsDeleted);
            if (recipe == null)
                return null;

            var published = await _context.Recipes.VisibleTo(_context, null).AnyAsync(r => r.Id == recipe.Id);
            var result = new RecipeImageSubmissionResultModel();

            foreach (var c in submission.Candidates.Take(MaxCandidatesPerSubmission))
            {
                var outcome = new RecipeImageCandidateOutcomeModel { SourceKey = c.SourceKey, SourceId = c.SourceId };
                result.Outcomes.Add(outcome);

                var problem = Validate(c);
                if (problem != null)
                {
                    outcome.Outcome = "rejected";
                    outcome.Message = problem;
                    continue;
                }

                if (!string.IsNullOrEmpty(recipe.Image))
                {
                    outcome.Outcome = "rejected";
                    outcome.Message = "Recipe already has an image.";
                    continue;
                }

                var exists = await _context.RecipeImageCandidates
                    .AnyAsync(x => x.RecipeId == recipe.Id && x.SourceKey == c.SourceKey && x.SourceId == c.SourceId);
                if (exists)
                {
                    outcome.Outcome = "duplicate";
                    continue;
                }

                byte[]? jpeg = null;
                if (!c.Hotlink)
                {
                    jpeg = NormalizeImage(c.ImageBase64!);
                    if (jpeg == null)
                    {
                        outcome.Outcome = "rejected";
                        outcome.Message = "Image bytes are missing, too large or not a decodable image.";
                        continue;
                    }
                }

                var attach = c.Status == RecipeImageCandidateStatus.Attached && published;
                var entity = new RecipeImageCandidateEntity
                {
                    RecipeId = recipe.Id,
                    Status = RecipeImageCandidateStatus.Pending,
                    SourceKey = c.SourceKey,
                    SourceName = Clip(c.SourceName, 64) ?? c.SourceKey,
                    SourceId = c.SourceId.Trim(),
                    Title = Clip(c.Title, 511),
                    Author = Clip(c.Author, 255),
                    AuthorUrl = SafeUrl(c.AuthorUrl, 1023),
                    License = Clip(c.License, 64)!,
                    LicenseCode = c.LicenseCode,
                    LicenseUrl = SafeUrl(c.LicenseUrl, 1023),
                    LandingUrl = SafeUrl(c.LandingUrl, 1023),
                    ImageUrl = SafeUrl(c.ImageUrl, 2047),
                    Hotlink = c.Hotlink,
                    DownloadLocation = c.Hotlink ? SafeUrl(c.DownloadLocation, 2047) : null,
                    Score = c.Score is { } s ? Math.Clamp(Math.Round(s, 4), 0m, 9.9999m) : null,
                    ChecksJson = c.Checks?.GetRawText(),
                    Batch = Clip(submission.Batch, 128),
                    CreatedDate = DateTime.UtcNow,
                    CreatedByPersonId = personId,
                };

                if (jpeg != null)
                {
                    entity.ContentType = "image/jpeg";
                    if (_mediaStorage.IsConfigured)
                        entity.FilePath = await _mediaStorage.SaveAsync($"recipe-image-candidates/{recipe.Id}/{Guid.NewGuid():N}.jpg", jpeg);
                    else
                        entity.FileData = jpeg;
                }

                _context.RecipeImageCandidates.Add(entity);
                await _context.SaveChangesAsync();
                outcome.CandidateId = entity.Id;

                if (attach)
                {
                    await AttachAsync(entity, recipe, RecipeImageCandidateStatus.Attached, personId);
                    outcome.Outcome = RecipeImageCandidateStatus.Attached;
                    _logger.LogInformation("Image finder attached {Source}:{SourceId} to recipe {RecipeId}", c.SourceKey, c.SourceId, recipe.Id);
                }
                else
                {
                    outcome.Outcome = RecipeImageCandidateStatus.Pending;
                }
            }

            return result;
        }

        public async Task<List<RecipeImageCandidateModel>> GetCandidatesAsync(string status, int limit)
        {
            limit = Math.Clamp(limit, 1, 500);
            var query = _context.RecipeImageCandidates.AsNoTracking().Where(c => c.Status == status);
            query = status == RecipeImageCandidateStatus.Pending
                ? query.OrderBy(c => c.RecipeId).ThenByDescending(c => c.Score)
                : query.OrderByDescending(c => c.ReviewedAt ?? c.CreatedDate);

            return await query
                .Take(limit)
                .Select(c => new RecipeImageCandidateModel
                {
                    Id = c.Id,
                    RecipeId = c.RecipeId,
                    RecipeName = c.Recipe != null ? c.Recipe.Name : string.Empty,
                    Status = c.Status,
                    SourceKey = c.SourceKey,
                    SourceName = c.SourceName,
                    Title = c.Title,
                    Author = c.Author,
                    AuthorUrl = c.AuthorUrl,
                    License = c.License,
                    LicenseUrl = c.LicenseUrl,
                    LandingUrl = c.LandingUrl,
                    Hotlink = c.Hotlink,
                    PreviewUrl = c.Hotlink ? c.ImageUrl : null,
                    Score = c.Score,
                    ChecksJson = c.ChecksJson,
                    CreatedDate = c.CreatedDate,
                    ReviewedAt = c.ReviewedAt,
                })
                .ToListAsync();
        }

        public async Task<(byte[] Data, string ContentType)?> GetCandidateImageAsync(long candidateId)
        {
            var c = await _context.RecipeImageCandidates.AsNoTracking().FirstOrDefaultAsync(x => x.Id == candidateId);
            if (c == null || c.Hotlink)
                return null;

            if (!string.IsNullOrEmpty(c.FilePath))
            {
                var data = await _mediaStorage.ReadAsync(c.FilePath);
                if (data != null)
                    return (data, c.ContentType ?? "image/jpeg");
            }
            else if (c.FileData is { Length: > 0 })
            {
                return (c.FileData, c.ContentType ?? "image/jpeg");
            }

            if (c.RecipeAssetId is { } assetId)
            {
                var asset = await _context.RecipeAssets.AsNoTracking().FirstOrDefaultAsync(a => a.Id == assetId);
                if (asset != null)
                {
                    var data = string.IsNullOrEmpty(asset.FilePath) ? asset.FileData : await _mediaStorage.ReadAsync(asset.FilePath);
                    if (data is { Length: > 0 })
                        return (data, asset.ContentType ?? "image/jpeg");
                }
            }

            return null;
        }

        public async Task<RecipeImageReviewResult> ApproveAsync(long candidateId, long? personId)
        {
            var c = await _context.RecipeImageCandidates.FirstOrDefaultAsync(x => x.Id == candidateId);
            if (c == null)
                return RecipeImageReviewResult.NotFound;
            if (c.Status != RecipeImageCandidateStatus.Pending)
                return RecipeImageReviewResult.NotPending;

            var recipe = await _context.Recipes.FirstOrDefaultAsync(r => r.Id == c.RecipeId);
            if (recipe == null)
                return RecipeImageReviewResult.NotFound;
            if (!string.IsNullOrEmpty(recipe.Image))
                return RecipeImageReviewResult.RecipeHasImage;

            await AttachAsync(c, recipe, RecipeImageCandidateStatus.Approved, personId);
            return RecipeImageReviewResult.Ok;
        }

        public async Task<RecipeImageReviewResult> RejectAsync(long candidateId, long? personId)
        {
            var c = await _context.RecipeImageCandidates.FirstOrDefaultAsync(x => x.Id == candidateId);
            if (c == null)
                return RecipeImageReviewResult.NotFound;
            if (c.Status != RecipeImageCandidateStatus.Pending)
                return RecipeImageReviewResult.NotPending;

            c.Status = RecipeImageCandidateStatus.Rejected;
            c.ReviewedAt = DateTime.UtcNow;
            c.ReviewedByPersonId = personId;
            await DiscardBytesAsync(c);
            await _context.SaveChangesAsync();
            return RecipeImageReviewResult.Ok;
        }

        public async Task<RecipeImageReviewResult> RemoveAsync(long candidateId, long? personId)
        {
            var c = await _context.RecipeImageCandidates.FirstOrDefaultAsync(x => x.Id == candidateId);
            if (c == null)
                return RecipeImageReviewResult.NotFound;
            if (c.Status is not (RecipeImageCandidateStatus.Attached or RecipeImageCandidateStatus.Approved))
                return RecipeImageReviewResult.NotPending;

            var recipe = await _context.Recipes.FirstOrDefaultAsync(r => r.Id == c.RecipeId);
            if (c.RecipeAssetId is { } assetId)
            {
                var asset = await _context.RecipeAssets.FirstOrDefaultAsync(a => a.Id == assetId);
                if (asset != null)
                {
                    if (!string.IsNullOrEmpty(asset.FilePath))
                        await _mediaStorage.DeleteAsync(asset.FilePath);
                    _context.RecipeAssets.Remove(asset);
                }
            }

            if (recipe != null && IsShowing(recipe, c))
            {
                recipe.Image = null;
                ClearCredit(recipe);
                recipe.LastModifiedDate = DateTime.UtcNow;
            }

            c.Status = RecipeImageCandidateStatus.Removed;
            c.ReviewedAt = DateTime.UtcNow;
            c.ReviewedByPersonId = personId;
            await _context.SaveChangesAsync();
            return RecipeImageReviewResult.Ok;
        }

        public async Task<List<RecipeImageDownloadTrackingModel>> GetPendingDownloadTrackingAsync()
        {
            return await _context.RecipeImageCandidates.AsNoTracking()
                .Where(c => c.Hotlink && c.DownloadLocation != null && c.DownloadTrackedAt == null
                    && (c.Status == RecipeImageCandidateStatus.Attached || c.Status == RecipeImageCandidateStatus.Approved))
                .OrderBy(c => c.Id)
                .Select(c => new RecipeImageDownloadTrackingModel { Id = c.Id, DownloadLocation = c.DownloadLocation! })
                .ToListAsync();
        }

        public async Task<bool> MarkDownloadTrackedAsync(long candidateId)
        {
            var c = await _context.RecipeImageCandidates.FirstOrDefaultAsync(x => x.Id == candidateId);
            if (c == null)
                return false;
            c.DownloadTrackedAt ??= DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return true;
        }

        /// <summary>Why a submitted candidate cannot be stored, or null when it can.</summary>
        public static string? Validate(RecipeImageCandidateSubmissionModel c)
        {
            if (!Sources.Contains(c.SourceKey ?? string.Empty))
                return $"Unknown source '{c.SourceKey}'.";
            if (string.IsNullOrWhiteSpace(c.SourceId) || c.SourceId.Length > 255)
                return "A source id is required.";
            if (!LicenseCodes.Contains(c.LicenseCode ?? string.Empty))
                return $"Licence '{c.LicenseCode}' is not accepted.";
            if (string.IsNullOrWhiteSpace(c.License))
                return "A licence label is required.";
            if (AttributionRequired.Contains(c.LicenseCode!) && string.IsNullOrWhiteSpace(c.Author))
                return "This licence requires an author to credit.";
            if (SafeUrl(c.LandingUrl, 1023) == null)
                return "A source page URL is required for the credit link.";
            if (c.Status is not (RecipeImageCandidateStatus.Attached or RecipeImageCandidateStatus.Pending))
                return "Status must be 'attached' or 'pending'.";

            var mustHotlink = c.SourceKey == HotlinkSource;
            if (c.Hotlink != mustHotlink)
                return mustHotlink ? "Unsplash images must be hotlinked." : "Only Unsplash images may be hotlinked.";
            if (c.Hotlink)
            {
                if (c.ImageUrl == null || !c.ImageUrl.StartsWith(HotlinkPrefix, StringComparison.Ordinal) || c.ImageUrl.Length > 2047)
                    return "Hotlinked images must be served from images.unsplash.com.";
                if (!string.IsNullOrEmpty(c.ImageBase64))
                    return "Hotlinked images must not be uploaded.";
            }
            else if (string.IsNullOrEmpty(c.ImageBase64))
            {
                return "Rehosted images must include the image bytes.";
            }

            return null;
        }

        public static RecipeImageCreditModel? CreditFor(RecipeEntity recipe)
        {
            if (string.IsNullOrEmpty(recipe.Image) || string.IsNullOrEmpty(recipe.ImageSourceName))
                return null;
            return new RecipeImageCreditModel
            {
                SourceName = recipe.ImageSourceName,
                SourceUrl = recipe.ImageSourceUrl,
                Author = recipe.ImageAuthor,
                AuthorUrl = recipe.ImageAuthorUrl,
                License = recipe.ImageLicense,
                LicenseUrl = recipe.ImageLicenseUrl,
                NoDerivatives = recipe.ImageLicense != null && recipe.ImageLicense.Contains("ND", StringComparison.Ordinal),
            };
        }

        public static void ClearCredit(RecipeEntity recipe)
        {
            recipe.ImageSourceName = null;
            recipe.ImageSourceUrl = null;
            recipe.ImageAuthor = null;
            recipe.ImageAuthorUrl = null;
            recipe.ImageLicense = null;
            recipe.ImageLicenseUrl = null;
        }

        private async Task AttachAsync(RecipeImageCandidateEntity c, RecipeEntity recipe, string status, long? personId)
        {
            if (c.Hotlink)
            {
                recipe.Image = c.ImageUrl;
            }
            else
            {
                var asset = new RecipeAssetEntity
                {
                    RecipeId = recipe.Id,
                    Name = $"{c.SourceKey}-{c.Id}.jpg",
                    FileExtension = ".jpg",
                    Icon = "image",
                    FilePath = c.FilePath,
                    FileData = c.FileData ?? Array.Empty<byte>(),
                    ContentType = c.ContentType ?? "image/jpeg",
                    FileSize = c.FileData?.Length ?? 0,
                    Description = $"{c.License} — {c.SourceName}",
                    CreatedDate = DateTime.UtcNow,
                };
                _context.RecipeAssets.Add(asset);
                await _context.SaveChangesAsync();
                c.RecipeAssetId = asset.Id;
                c.FilePath = null;
                c.FileData = null;
                recipe.Image = $"/api/recipe/{recipe.Id}/image";
            }

            recipe.ImageSourceName = c.SourceName;
            recipe.ImageSourceUrl = c.LandingUrl;
            recipe.ImageAuthor = c.Author;
            recipe.ImageAuthorUrl = c.AuthorUrl;
            recipe.ImageLicense = c.License;
            recipe.ImageLicenseUrl = c.LicenseUrl;
            recipe.LastModifiedDate = DateTime.UtcNow;

            c.Status = status;
            c.ReviewedAt = DateTime.UtcNow;
            c.ReviewedByPersonId = personId;

            var siblings = await _context.RecipeImageCandidates
                .Where(x => x.RecipeId == recipe.Id && x.Id != c.Id && x.Status == RecipeImageCandidateStatus.Pending)
                .ToListAsync();
            foreach (var sibling in siblings)
            {
                sibling.Status = RecipeImageCandidateStatus.Superseded;
                sibling.ReviewedAt = DateTime.UtcNow;
                await DiscardBytesAsync(sibling);
            }

            await _context.SaveChangesAsync();
        }

        private async Task DiscardBytesAsync(RecipeImageCandidateEntity c)
        {
            if (!string.IsNullOrEmpty(c.FilePath))
                await _mediaStorage.DeleteAsync(c.FilePath);
            c.FilePath = null;
            c.FileData = null;
        }

        private static bool IsShowing(RecipeEntity recipe, RecipeImageCandidateEntity c) =>
            c.Hotlink
                ? recipe.Image == c.ImageUrl
                : recipe.Image == $"/api/recipe/{recipe.Id}/image" && recipe.ImageSourceUrl == c.LandingUrl;

        private static byte[]? NormalizeImage(string base64)
        {
            byte[] raw;
            try
            {
                raw = Convert.FromBase64String(base64);
            }
            catch (FormatException)
            {
                return null;
            }
            if (raw.Length == 0 || raw.Length > MaxImageBytes)
                return null;

            return ImageTranscoder.ToJpeg(raw, MaxWidth);
        }

        private static string? Clip(string? value, int max)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;
            var trimmed = value.Trim();
            return trimmed.Length <= max ? trimmed : trimmed[..max];
        }

        private static string? SafeUrl(string? value, int max)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > max)
                return null;
            var trimmed = value.Trim();
            return Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
                ? trimmed
                : null;
        }
    }
}

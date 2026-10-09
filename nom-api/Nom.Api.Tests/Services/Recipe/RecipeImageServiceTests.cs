using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Nom.Api.Controllers;
using Nom.Data;
using Nom.Data.Recipe;
using Nom.Data.Reference;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Recipe;
using Nom.Orch.Services;
using Nom.Orch.UtilityInterfaces;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nom.Api.Tests.Services.Recipe
{
    /// <summary>
    /// The image finder's landing zone: only published recipes without an image are offered,
    /// fully verified photos go live with their credit line, borderline ones wait for an admin,
    /// and licences or hosting modes the determination doc rules out are refused at the API.
    /// </summary>
    public class RecipeImageServiceTests
    {
        private const long Approved = (long)CurationStatusEnum.Curated;
        private const long Pending = (long)CurationStatusEnum.PendingCuration;
        private const long Admin = 5;

        private sealed class MemoryMedia : IMediaStorageService
        {
            public Dictionary<string, byte[]> Files { get; } = new();
            public bool IsConfigured { get; init; }
            public Task<string> SaveAsync(string relativePath, byte[] data) { Files[relativePath] = data; return Task.FromResult(relativePath); }
            public Task<byte[]?> ReadAsync(string relativePath) => Task.FromResult(Files.TryGetValue(relativePath, out var d) ? d : null);
            public Task DeleteAsync(string relativePath) { Files.Remove(relativePath); return Task.CompletedTask; }
        }

        private static async Task<ApplicationDbContext> SeedAsync()
        {
            var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            db.Set<ReferenceEntity>().AddRange(
                new ReferenceEntity { Id = Approved, Name = "Approved" },
                new ReferenceEntity { Id = Pending, Name = "Pending Curation" });
            var soup = new RecipeEntity { Id = 1, Name = "Creamy Tomato Basil Soup", Description = "Velvety soup.", CurationStatusId = Approved, Visibility = RecipeVisibilityEnum.Public, AuthorId = 9 };
            var curry = new RecipeEntity { Id = 2, Name = "Coconut Shrimp Curry", CurationStatusId = Approved, Visibility = RecipeVisibilityEnum.Public, AuthorId = 9 };
            var pictured = new RecipeEntity { Id = 3, Name = "Pictured Pancakes", CurationStatusId = Approved, Visibility = RecipeVisibilityEnum.Public, AuthorId = 9, Image = "/api/recipe/3/image" };
            var draft = new RecipeEntity { Id = 4, Name = "Draft Stew", CurationStatusId = Pending, Visibility = RecipeVisibilityEnum.Public, AuthorId = 9 };
            var hidden = new RecipeEntity { Id = 5, Name = "Private Toast", CurationStatusId = Approved, Visibility = RecipeVisibilityEnum.Private, AuthorId = 9 };
            db.Recipes.AddRange(soup, curry, pictured, draft, hidden);
            db.Ingredients.AddRange(new IngredientEntity { Id = 50, Name = "Tomato" }, new IngredientEntity { Id = 51, Name = "Basil" });
            db.RecipeIngredients.AddRange(
                new RecipeIngredientEntity { Id = 70, RecipeId = 1, IngredientId = 50, RawLine = "4 tomatoes" },
                new RecipeIngredientEntity { Id = 71, RecipeId = 1, IngredientId = 51, RawLine = "basil" });
            db.RecipeSteps.Add(new RecipeStepEntity { Id = 80, RecipeId = 1, StepNumber = 1, Summary = "Simmer", Description = "Simmer and blend." });
            db.RecipeRatings.Add(new RecipeRatingEntity { RecipeId = 2, RaterId = 9, Rating = 5 });
            await db.SaveChangesAsync();
            return db;
        }

        private static RecipeImageService NewService(ApplicationDbContext db, MemoryMedia? media = null) =>
            new(db, media ?? new MemoryMedia(), NullLogger<RecipeImageService>.Instance);

        private static string Png(int width, int height)
        {
            using var image = new Image<Rgba32>(width, height, new Rgba32(200, 60, 40));
            using var ms = new MemoryStream();
            image.SaveAsPng(ms);
            return Convert.ToBase64String(ms.ToArray());
        }

        private static RecipeImageCandidateSubmissionModel Commons(string id = "File:Tomato soup.jpg", string status = "attached") => new()
        {
            SourceKey = "wikimedia",
            SourceName = "Wikimedia Commons",
            SourceId = id,
            Title = "Tomato soup",
            Author = "Jane Cook",
            AuthorUrl = "https://commons.wikimedia.org/wiki/User:Jane",
            License = "CC BY 4.0",
            LicenseCode = "by",
            LicenseUrl = "https://creativecommons.org/licenses/by/4.0",
            LandingUrl = "https://commons.wikimedia.org/wiki/" + id.Replace(' ', '_'),
            ImageUrl = "https://upload.wikimedia.org/x.jpg",
            Score = 0.91m,
            Checks = JsonDocument.Parse("{\"metadata\":{\"passed\":true,\"reasons\":[]}}").RootElement,
            Status = status,
            ImageBase64 = Png(1600, 900),
        };

        private static RecipeImageCandidateSubmissionModel Unsplash(string status = "attached") => new()
        {
            SourceKey = "unsplash",
            SourceName = "Unsplash",
            SourceId = "abc123",
            Author = "Sam Lens",
            AuthorUrl = "https://unsplash.com/@sam?utm_source=nom&utm_medium=referral",
            License = "Unsplash License",
            LicenseCode = "unsplash",
            LandingUrl = "https://unsplash.com/photos/abc123?utm_source=nom&utm_medium=referral",
            ImageUrl = "https://images.unsplash.com/photo-1?ixid=x&w=1080",
            Hotlink = true,
            DownloadLocation = "https://api.unsplash.com/photos/abc123/download?ixid=x",
            Status = status,
        };

        private static RecipeImageSubmissionModel For(long recipeId, params RecipeImageCandidateSubmissionModel[] candidates) =>
            new() { RecipeId = recipeId, Batch = "test", Candidates = candidates.ToList() };

        [Fact]
        public async Task Missing_lists_only_published_recipes_without_an_image_most_rated_first()
        {
            using var db = await SeedAsync();
            var missing = await NewService(db).GetMissingAsync(10);

            missing.Select(m => m.Id).Should().Equal(2, 1);
            var soup = missing.Single(m => m.Id == 1);
            soup.Ingredients.Should().Equal("Tomato", "Basil");
            soup.Steps.Should().Equal("Simmer and blend.");
        }

        [Fact]
        public async Task Missing_skips_recipes_waiting_in_the_queue_and_reports_rejected_photos()
        {
            using var db = await SeedAsync();
            var service = NewService(db);
            await service.SubmitAsync(For(1, Commons(status: "pending")), Admin);
            await service.SubmitAsync(For(2, Commons("File:Curry.jpg", "pending")), Admin);
            var curryCandidate = db.RecipeImageCandidates.Single(c => c.RecipeId == 2);
            await service.RejectAsync(curryCandidate.Id, Admin);

            var missing = await service.GetMissingAsync(10);

            missing.Select(m => m.Id).Should().Equal(2);
            missing.Single().RejectedCandidates.Should().Equal("wikimedia:File:Curry.jpg");
        }

        [Fact]
        public async Task A_fully_verified_photo_is_rehosted_resized_and_credited()
        {
            using var db = await SeedAsync();
            var result = await NewService(db).SubmitAsync(For(1, Commons()), Admin);

            result!.Outcomes.Single().Outcome.Should().Be("attached");
            var recipe = db.Recipes.Single(r => r.Id == 1);
            recipe.Image.Should().Be("/api/recipe/1/image");
            recipe.ImageAuthor.Should().Be("Jane Cook");
            recipe.ImageLicense.Should().Be("CC BY 4.0");
            recipe.ImageSourceName.Should().Be("Wikimedia Commons");

            var asset = db.RecipeAssets.Single(a => a.RecipeId == 1);
            asset.ContentType.Should().Be("image/jpeg");
            using var stored = Image.Load(asset.FileData);
            stored.Width.Should().Be(RecipeImageService.MaxWidth);

            var candidate = db.RecipeImageCandidates.Single();
            candidate.Status.Should().Be(RecipeImageCandidateStatus.Attached);
            candidate.RecipeAssetId.Should().Be(asset.Id);
            candidate.FileData.Should().BeNull();
            candidate.ChecksJson.Should().Contain("metadata");

            var credit = RecipeImageService.CreditFor(recipe)!;
            credit.Author.Should().Be("Jane Cook");
            credit.NoDerivatives.Should().BeFalse();
        }

        [Fact]
        public async Task Pending_bytes_go_to_the_media_store_when_one_is_configured()
        {
            using var db = await SeedAsync();
            var media = new MemoryMedia { IsConfigured = true };
            var service = NewService(db, media);
            await service.SubmitAsync(For(1, Commons(status: "pending")), Admin);

            var candidate = db.RecipeImageCandidates.Single();
            candidate.FilePath.Should().StartWith("recipe-image-candidates/1/");
            media.Files.Should().ContainKey(candidate.FilePath!);
            (await service.GetCandidateImageAsync(candidate.Id))!.Value.ContentType.Should().Be("image/jpeg");

            await service.RejectAsync(candidate.Id, Admin);
            media.Files.Should().BeEmpty();
        }

        [Fact]
        public async Task Unsplash_is_hotlinked_not_rehosted_and_its_download_is_tracked_once()
        {
            using var db = await SeedAsync();
            var service = NewService(db);
            await service.SubmitAsync(For(2, Unsplash()), Admin);

            db.Recipes.Single(r => r.Id == 2).Image.Should().Be("https://images.unsplash.com/photo-1?ixid=x&w=1080");
            db.RecipeAssets.Should().BeEmpty();

            var tracking = await service.GetPendingDownloadTrackingAsync();
            tracking.Should().ContainSingle().Which.DownloadLocation.Should().StartWith("https://api.unsplash.com/");
            (await service.MarkDownloadTrackedAsync(tracking[0].Id)).Should().BeTrue();
            (await service.GetPendingDownloadTrackingAsync()).Should().BeEmpty();
        }

        [Fact]
        public async Task An_attached_request_for_an_unpublished_recipe_waits_for_review()
        {
            using var db = await SeedAsync();
            var result = await NewService(db).SubmitAsync(For(4, Commons()), Admin);

            result!.Outcomes.Single().Outcome.Should().Be("pending");
            db.Recipes.Single(r => r.Id == 4).Image.Should().BeNull();
        }

        [Fact]
        public async Task Recipes_that_already_have_an_image_are_never_overwritten()
        {
            using var db = await SeedAsync();
            var result = await NewService(db).SubmitAsync(For(3, Commons()), Admin);

            result!.Outcomes.Single().Outcome.Should().Be("rejected");
            db.Recipes.Single(r => r.Id == 3).Image.Should().Be("/api/recipe/3/image");
            db.RecipeImageCandidates.Should().BeEmpty();
        }

        [Fact]
        public async Task The_same_photo_is_stored_once_per_recipe()
        {
            using var db = await SeedAsync();
            var service = NewService(db);
            await service.SubmitAsync(For(1, Commons(status: "pending")), Admin);
            var again = await service.SubmitAsync(For(1, Commons(status: "pending")), Admin);

            again!.Outcomes.Single().Outcome.Should().Be("duplicate");
            db.RecipeImageCandidates.Should().ContainSingle();
        }

        public static IEnumerable<object[]> RefusedSubmissions()
        {
            yield return new object[] { "non-commercial licence", (Action<RecipeImageCandidateSubmissionModel>)(c => c.LicenseCode = "nc") };
            yield return new object[] { "GFDL", (Action<RecipeImageCandidateSubmissionModel>)(c => c.LicenseCode = "gfdl") };
            yield return new object[] { "unknown source", (Action<RecipeImageCandidateSubmissionModel>)(c => c.SourceKey = "google") };
            yield return new object[] { "hotlinking a rehost-only source", (Action<RecipeImageCandidateSubmissionModel>)(c => c.Hotlink = true) };
            yield return new object[] { "CC BY without an author", (Action<RecipeImageCandidateSubmissionModel>)(c => c.Author = " ") };
            yield return new object[] { "script URL as credit link", (Action<RecipeImageCandidateSubmissionModel>)(c => c.LandingUrl = "javascript:alert(1)") };
            yield return new object[] { "no image bytes", (Action<RecipeImageCandidateSubmissionModel>)(c => c.ImageBase64 = null) };
            yield return new object[] { "unknown status", (Action<RecipeImageCandidateSubmissionModel>)(c => c.Status = "approved") };
        }

        [Theory]
        [MemberData(nameof(RefusedSubmissions))]
        public void Validation_refuses_what_the_determination_doc_rules_out(string because, Action<RecipeImageCandidateSubmissionModel> spoil)
        {
            var candidate = Commons();
            spoil(candidate);
            RecipeImageService.Validate(candidate).Should().NotBeNull(because);
        }

        [Fact]
        public void Unsplash_must_be_hotlinked_from_its_own_cdn()
        {
            RecipeImageService.Validate(Unsplash()).Should().BeNull();

            var rehosted = Unsplash();
            rehosted.Hotlink = false;
            rehosted.ImageBase64 = Png(10, 10);
            RecipeImageService.Validate(rehosted).Should().NotBeNull();

            var elsewhere = Unsplash();
            elsewhere.ImageUrl = "https://evil.example/photo.jpg";
            RecipeImageService.Validate(elsewhere).Should().NotBeNull();
        }

        [Fact]
        public async Task Undecodable_bytes_are_refused()
        {
            using var db = await SeedAsync();
            var bad = Commons();
            bad.ImageBase64 = Convert.ToBase64String(new byte[] { 1, 2, 3, 4 });

            var result = await NewService(db).SubmitAsync(For(1, bad), Admin);

            result!.Outcomes.Single().Outcome.Should().Be("rejected");
            db.Recipes.Single(r => r.Id == 1).Image.Should().BeNull();
        }

        [Fact]
        public async Task Approving_attaches_the_photo_and_supersedes_its_siblings()
        {
            using var db = await SeedAsync();
            var service = NewService(db);
            await service.SubmitAsync(For(1, Commons("File:A.jpg", "pending"), Commons("File:B.jpg", "pending")), Admin);
            var first = db.RecipeImageCandidates.Single(c => c.SourceId == "File:A.jpg");

            (await service.ApproveAsync(first.Id, Admin)).Should().Be(RecipeImageReviewResult.Ok);

            db.Recipes.Single(r => r.Id == 1).Image.Should().Be("/api/recipe/1/image");
            first.Status.Should().Be(RecipeImageCandidateStatus.Approved);
            first.ReviewedByPersonId.Should().Be(Admin);
            db.RecipeImageCandidates.Single(c => c.SourceId == "File:B.jpg").Status.Should().Be(RecipeImageCandidateStatus.Superseded);
            (await service.ApproveAsync(first.Id, Admin)).Should().Be(RecipeImageReviewResult.NotPending);
        }

        [Fact]
        public async Task Approval_never_replaces_an_image_the_recipe_got_meanwhile()
        {
            using var db = await SeedAsync();
            var service = NewService(db);
            await service.SubmitAsync(For(1, Commons(status: "pending")), Admin);
            db.Recipes.Single(r => r.Id == 1).Image = "/api/recipe/1/image";
            await db.SaveChangesAsync();

            (await service.ApproveAsync(db.RecipeImageCandidates.Single().Id, Admin)).Should().Be(RecipeImageReviewResult.RecipeHasImage);
        }

        [Fact]
        public async Task Removing_an_attached_photo_takes_it_and_its_credit_off_the_recipe()
        {
            using var db = await SeedAsync();
            var service = NewService(db);
            await service.SubmitAsync(For(1, Commons()), Admin);
            var candidate = db.RecipeImageCandidates.Single();

            (await service.RemoveAsync(candidate.Id, Admin)).Should().Be(RecipeImageReviewResult.Ok);

            var recipe = db.Recipes.Single(r => r.Id == 1);
            recipe.Image.Should().BeNull();
            recipe.ImageAuthor.Should().BeNull();
            recipe.ImageLicense.Should().BeNull();
            db.RecipeAssets.Should().BeEmpty();
            candidate.Status.Should().Be(RecipeImageCandidateStatus.Removed);
            RecipeImageService.CreditFor(recipe).Should().BeNull();
        }

        [Fact]
        public async Task No_derivatives_licences_are_flagged_for_uncropped_display()
        {
            using var db = await SeedAsync();
            var nd = Commons();
            nd.LicenseCode = "by-nd";
            nd.License = "CC BY-ND 2.0";
            await NewService(db).SubmitAsync(For(1, nd), Admin);

            RecipeImageService.CreditFor(db.Recipes.Single(r => r.Id == 1))!.NoDerivatives.Should().BeTrue();
        }

        [Fact]
        public async Task Controller_maps_review_results_to_http_statuses()
        {
            var service = new Mock<IRecipeImageService>();
            service.Setup(s => s.ApproveAsync(1, It.IsAny<long?>())).ReturnsAsync(RecipeImageReviewResult.RecipeHasImage);
            service.Setup(s => s.ApproveAsync(2, It.IsAny<long?>())).ReturnsAsync(RecipeImageReviewResult.Ok);
            service.Setup(s => s.RejectAsync(3, It.IsAny<long?>())).ReturnsAsync(RecipeImageReviewResult.NotFound);
            var controller = new RecipeImagesController(service.Object)
            {
                ControllerContext = new ControllerContext { HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext() },
            };

            (await controller.Approve(1)).Should().BeOfType<ConflictObjectResult>();
            (await controller.Approve(2)).Should().BeOfType<NoContentResult>();
            (await controller.Reject(3)).Should().BeOfType<NotFoundResult>();
            (await controller.GetCandidates("everything")).Result.Should().BeOfType<BadRequestObjectResult>();
            (await controller.Submit(new RecipeImageSubmissionModel { RecipeId = 1 })).Result.Should().BeOfType<BadRequestObjectResult>();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Nom.Data;
using Nom.Data.Recipe;
using Nom.Data.Reference;
using Nom.Orch.Models.Recipe;
using Nom.Orch.Services;
using Nom.Orch.UtilityInterfaces;
using Xunit;

namespace Nom.Api.Tests.Services.Recipe
{
    /// <summary>
    /// RecipeImages:MaxPendingReview: once the review queue holds that many candidates the finder's
    /// borderline photos are held back instead of stored, while verified attaches still go live and
    /// the held recipe returns to the work list after its retry period. No setting means no cap.
    /// </summary>
    public class RecipeImageReviewCapTests
    {
        private const long Approved = (long)CurationStatusEnum.Curated;
        private const long Pending = (long)CurationStatusEnum.PendingCuration;
        private const long Admin = 5;

        private sealed class NoMedia : IMediaStorageService
        {
            public bool IsConfigured => false;
            public Task<string> SaveAsync(string relativePath, byte[] data) => Task.FromResult(relativePath);
            public Task<byte[]?> ReadAsync(string relativePath) => Task.FromResult<byte[]?>(null);
            public Task DeleteAsync(string relativePath) => Task.CompletedTask;
        }

        private static async Task<ApplicationDbContext> SeedAsync(int queued)
        {
            var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            db.Set<ReferenceEntity>().AddRange(
                new ReferenceEntity { Id = Approved, Name = "Approved" },
                new ReferenceEntity { Id = Pending, Name = "Pending Curation" });
            db.Recipes.AddRange(
                new RecipeEntity { Id = 1, Name = "Creamy Tomato Basil Soup", CurationStatusId = Approved, Visibility = RecipeVisibilityEnum.Public, AuthorId = 9 },
                new RecipeEntity { Id = 2, Name = "Coconut Shrimp Curry", CurationStatusId = Approved, Visibility = RecipeVisibilityEnum.Public, AuthorId = 9 },
                new RecipeEntity { Id = 4, Name = "Draft Stew", CurationStatusId = Pending, Visibility = RecipeVisibilityEnum.Public, AuthorId = 9 },
                new RecipeEntity { Id = 10, Name = "Queued Pie", CurationStatusId = Approved, Visibility = RecipeVisibilityEnum.Public, AuthorId = 9 });
            for (var i = 0; i < queued; i++)
            {
                db.RecipeImageCandidates.Add(new RecipeImageCandidateEntity
                {
                    RecipeId = 10, Status = RecipeImageCandidateStatus.Pending, SourceKey = "unsplash", SourceName = "Unsplash",
                    SourceId = $"queued-{i}", License = "Unsplash License", LicenseCode = "unsplash", Hotlink = true,
                    ImageUrl = $"https://images.unsplash.com/queued-{i}", CreatedDate = DateTime.UtcNow,
                });
            }
            await db.SaveChangesAsync();
            return db;
        }

        private static RecipeImageService NewService(ApplicationDbContext db, int? cap)
        {
            var values = new Dictionary<string, string?>();
            if (cap is { } c) values["RecipeImages:MaxPendingReview"] = c.ToString();
            var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
            return new RecipeImageService(db, new NoMedia(), NullLogger<RecipeImageService>.Instance, config);
        }

        private static RecipeImageCandidateSubmissionModel Photo(string id, string status) => new()
        {
            SourceKey = "unsplash",
            SourceName = "Unsplash",
            SourceId = id,
            Author = "Sam Lens",
            AuthorUrl = "https://unsplash.com/@sam?utm_source=nom&utm_medium=referral",
            License = "Unsplash License",
            LicenseCode = "unsplash",
            LandingUrl = $"https://unsplash.com/photos/{id}?utm_source=nom&utm_medium=referral",
            ImageUrl = $"https://images.unsplash.com/{id}?w=1080",
            Hotlink = true,
            DownloadLocation = $"https://api.unsplash.com/photos/{id}/download",
            Status = status,
        };

        private static RecipeImageSubmissionModel For(long recipeId, params RecipeImageCandidateSubmissionModel[] candidates) =>
            new() { RecipeId = recipeId, Batch = "test", Candidates = candidates.ToList() };

        private static int Queued(ApplicationDbContext db) =>
            db.RecipeImageCandidates.Count(c => c.Status == RecipeImageCandidateStatus.Pending);

        [Theory]
        [InlineData(null)]
        [InlineData(0)]
        [InlineData(-3)]
        public async Task Without_a_cap_the_queue_grows_as_before(int? cap)
        {
            using var db = await SeedAsync(queued: 5);
            var result = await NewService(db, cap).SubmitAsync(For(1, Photo("a", "pending")), Admin);

            result!.Outcomes.Single().Outcome.Should().Be("pending");
            Queued(db).Should().Be(6);
        }

        [Fact]
        public async Task A_full_queue_holds_a_borderline_photo_without_storing_it()
        {
            using var db = await SeedAsync(queued: 3);
            var result = await NewService(db, 3).SubmitAsync(For(1, Photo("a", "pending")), Admin);

            var outcome = result!.Outcomes.Single();
            outcome.Outcome.Should().Be(RecipeImageService.HeldOutcome);
            outcome.CandidateId.Should().BeNull();
            outcome.Message.Should().Contain("3");
            db.RecipeImageCandidates.Should().NotContain(c => c.RecipeId == 1);
            Queued(db).Should().Be(3);
        }

        [Fact]
        public async Task The_cap_is_a_ceiling_within_one_submission()
        {
            using var db = await SeedAsync(queued: 2);
            var result = await NewService(db, 3).SubmitAsync(For(1, Photo("a", "pending"), Photo("b", "pending"), Photo("c", "pending")), Admin);

            result!.Outcomes.Select(o => o.Outcome).Should().Equal("pending", "held", "held");
            Queued(db).Should().Be(3);
        }

        [Fact]
        public async Task A_verified_photo_still_attaches_when_the_queue_is_full()
        {
            using var db = await SeedAsync(queued: 3);
            var result = await NewService(db, 3).SubmitAsync(For(2, Photo("a", "attached")), Admin);

            result!.Outcomes.Single().Outcome.Should().Be(RecipeImageCandidateStatus.Attached);
            db.Recipes.Single(r => r.Id == 2).Image.Should().Be("https://images.unsplash.com/a?w=1080");
        }

        [Fact]
        public async Task An_attach_that_would_wait_for_review_is_held_too()
        {
            using var db = await SeedAsync(queued: 3);
            var result = await NewService(db, 3).SubmitAsync(For(4, Photo("a", "attached")), Admin);

            result!.Outcomes.Single().Outcome.Should().Be(RecipeImageService.HeldOutcome);
            db.Recipes.Single(r => r.Id == 4).Image.Should().BeNull();
            Queued(db).Should().Be(3);
        }

        [Fact]
        public async Task A_held_recipe_rests_once_searched_and_returns_after_the_retry_period()
        {
            using var db = await SeedAsync(queued: 3);
            var service = NewService(db, 3);
            await service.MarkSearchedAsync(new long[] { 1 });
            await service.SubmitAsync(For(1, Photo("a", "pending")), Admin);

            (await service.GetMissingAsync(50, retryDays: 90)).Select(r => r.Id).Should().NotContain(1);

            db.Recipes.Single(r => r.Id == 1).ImageSearchedAt = DateTime.UtcNow.AddDays(-91);
            await db.SaveChangesAsync();
            (await service.GetMissingAsync(50, retryDays: 90)).Select(r => r.Id).Should().Contain(1);
        }
    }
}

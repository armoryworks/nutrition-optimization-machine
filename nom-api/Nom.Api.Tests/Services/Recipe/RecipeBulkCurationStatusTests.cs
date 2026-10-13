using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Nom.Data;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Recipe;
using Nom.Orch.Services;
using Xunit;

namespace Nom.Api.Tests.Services.Recipe
{
    /// <summary>
    /// Bulk settings once wrote curation ids 1/2/3, which are not curation statuses at all. Authors
    /// may now submit or withdraw their own recipes in bulk; approving, rejecting or requesting
    /// revision stays a curator's decision.
    /// </summary>
    public class RecipeBulkCurationStatusTests
    {
        [Theory]
        [InlineData("pending", CurationStatusEnum.PendingCuration)]
        [InlineData("PendingCuration", CurationStatusEnum.PendingCuration)]
        [InlineData("submitted", CurationStatusEnum.PendingCuration)]
        [InlineData("noncurated", CurationStatusEnum.NonCurated)]
        [InlineData("Non-Curated", CurationStatusEnum.NonCurated)]
        [InlineData("draft", CurationStatusEnum.NonCurated)]
        public void Maps_author_statuses_to_real_curation_ids(string status, CurationStatusEnum expected)
        {
            RecipeBulkOperationsService.MapAuthorCurationStatus(status).Should().Be((long)expected);
        }

        [Theory]
        [InlineData("approved")]
        [InlineData("curated")]
        [InlineData("rejected")]
        [InlineData("requiresrevision")]
        [InlineData("bogus")]
        public void Curator_decisions_and_unknown_values_are_refused(string status)
        {
            RecipeBulkOperationsService.MapAuthorCurationStatus(status).Should().BeNull();
        }

        [Fact]
        public async Task Update_settings_submits_and_refuses_self_approval()
        {
            using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            db.Recipes.AddRange(
                new RecipeEntity { Id = 1, Name = "Mine", AuthorId = 7, CurationStatusId = (long)CurationStatusEnum.NonCurated },
                new RecipeEntity { Id = 2, Name = "Also mine", AuthorId = 7, CurationStatusId = (long)CurationStatusEnum.NonCurated });
            await db.SaveChangesAsync();
            var service = new RecipeBulkOperationsService(db, Mock.Of<IHttpContextAccessor>(), Mock.Of<ICurrentUserService>(),
                NullLogger<RecipeBulkOperationsService>.Instance);

            var submitted = await service.UpdateSettingsAsync(new RecipeBulkUpdateSettingsModel
            {
                RecipeIds = new List<long> { 1 }, RequesterPersonId = 7, CurationStatus = "pending",
            });
            var approved = await service.UpdateSettingsAsync(new RecipeBulkUpdateSettingsModel
            {
                RecipeIds = new List<long> { 2 }, RequesterPersonId = 7, CurationStatus = "approved",
            });

            submitted.SuccessCount.Should().Be(1);
            approved.SuccessCount.Should().Be(0);
            approved.Errors.Should().ContainSingle().Which.Should().Contain("curation queue");
            var statuses = await db.Recipes.OrderBy(r => r.Id).Select(r => r.CurationStatusId).ToListAsync();
            statuses.Should().Equal((long)CurationStatusEnum.PendingCuration, (long)CurationStatusEnum.NonCurated);
            (await db.Recipes.SingleAsync(r => r.Id == 1)).DateSubmittedForCuration.Should().NotBeNull();
        }
    }
}

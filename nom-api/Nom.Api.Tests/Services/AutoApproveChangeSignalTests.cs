using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Nom.Api.Services;
using Nom.Data;
using Nom.Data.Recipe;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// The auto-approval sweep is skipped while nothing it decides on has changed, so the
    /// fingerprint must move with every change that can turn a refusal into an approval.
    /// </summary>
    public class AutoApproveChangeSignalTests
    {
        [Fact]
        public async Task The_auto_approval_fingerprint_changes_when_an_ingredient_is_curated_and_not_otherwise()
        {
            using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            db.Ingredients.Add(new IngredientEntity { Id = 1, Name = "flour", CurationStatusId = (long)CurationStatusEnum.NonCurated });
            await db.SaveChangesAsync();
            var before = await AutoApproveChangeSignal.FingerprintAsync(db, default);
            (await AutoApproveChangeSignal.FingerprintAsync(db, default)).Should().Be(before);

            (await db.Ingredients.SingleAsync()).CurationStatusId = (long)CurationStatusEnum.Curated;
            await db.SaveChangesAsync();

            (await AutoApproveChangeSignal.FingerprintAsync(db, default)).Should().NotBe(before);
        }
    }
}

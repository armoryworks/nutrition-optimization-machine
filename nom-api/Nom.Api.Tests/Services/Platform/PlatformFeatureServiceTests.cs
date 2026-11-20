using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Nom.Data;
using Nom.Data.Person;
using Nom.Orch.Interfaces;
using Nom.Orch.Services;
using Xunit;

namespace Nom.Api.Tests.Services.Platform
{
    /// <summary>
    /// The first toggle of a key inserts its row with only Created* set, so the
    /// admin list must report who created it and when, not "never changed" (UA-140).
    /// </summary>
    public class PlatformFeatureServiceTests
    {
        private static ApplicationDbContext NewContext() =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        private static PlatformFeatureService NewService(ApplicationDbContext db) =>
            new(db, new MemoryCache(new MemoryCacheOptions()), NullLogger<PlatformFeatureService>.Instance);

        [Fact]
        public async Task First_toggle_reports_who_turned_it_on_with_the_built_in_label()
        {
            using var db = NewContext();
            db.Persons.Add(new PersonEntity { Id = 4, Name = "Avery Admin" });
            await db.SaveChangesAsync();
            var svc = NewService(db);

            await svc.SetAsync(IPlatformFeatureService.Keys.Brigade, true, 4);
            var listed = (await svc.ListAsync()).Should().ContainSingle(f => f.Key == IPlatformFeatureService.Keys.Brigade).Subject;

            listed.Label.Should().Be("Brigade");
            listed.Description.Should().NotBeNullOrWhiteSpace();
            listed.CreatedDate.Should().NotBeNull();
            listed.CreatedByName.Should().Be("Avery Admin");
            listed.LastModifiedDate.Should().BeNull();
        }

        [Fact]
        public async Task Later_toggle_reports_who_changed_it()
        {
            using var db = NewContext();
            db.Persons.Add(new PersonEntity { Id = 4, Name = "Avery Admin" });
            db.Persons.Add(new PersonEntity { Id = 7, Name = "Second Admin" });
            await db.SaveChangesAsync();
            var svc = NewService(db);

            await svc.SetAsync(IPlatformFeatureService.Keys.Brigade, true, 4);
            var updated = await svc.SetAsync(IPlatformFeatureService.Keys.Brigade, false, 7);

            updated.LastModifiedDate.Should().NotBeNull();
            updated.LastModifiedByName.Should().Be("Second Admin");
            updated.CreatedByName.Should().Be("Avery Admin");
        }

        [Fact]
        public async Task The_ai_pause_switches_are_listed_off_before_anyone_has_touched_them()
        {
            using var db = NewContext();
            var svc = NewService(db);

            var listed = await svc.ListAsync();

            listed.Should().Contain(f => f.Key == Nom.Orch.Services.Support.AiLanes.PauseAllKey && !f.IsEnabled && f.Label == "Pause all AI lanes" && f.CreatedDate == null);
            listed.Should().Contain(f => f.Key == "ai-pause-link" && !f.IsEnabled);
            (await svc.IsEnabledAsync("ai-pause-link")).Should().BeFalse("a missing row means the lane runs");
        }
    }
}

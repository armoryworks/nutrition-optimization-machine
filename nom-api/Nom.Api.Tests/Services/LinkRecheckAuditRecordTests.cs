using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Nom.Data;
using Nom.Data.Curation;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Curation;
using Nom.Orch.Services;
using Nom.Orch.Services.Support;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// The link re-check reads the link-audit record (scores/link-audit.json, written by the reader
    /// lanes' scorer): a set leaves shadow only on a record scored with the auditor and the
    /// confirming reader it is audited with, and the record's known-bad and known-good flags are the
    /// audit check's score the frame corrects with.
    /// </summary>
    public class LinkRecheckAuditRecordTests
    {
        private sealed class NoUser : ICurrentUserService
        {
            public long? PersonId => null;
            public long PersonIdOrSystem => SystemConstants.SystemPersonId;
            public long RequiredPersonId => throw new UnauthorizedAccessException();
            public string? UserId => null;
            public string RequiredUserId => throw new UnauthorizedAccessException();
        }

        private sealed class Reader : ILaneAuditor, ILaneAuditCheck
        {
            public Reader(string model) => Model = model;
            public string Model { get; }
            public string Name => Model;
            public Task<LaneAuditAnswer> AuditAsync(LaneDecisionEntity decision, CancellationToken cancellationToken) => Task.FromResult(new LaneAuditAnswer(false, "same"));
            public Task<bool?> ConfirmsWrongAsync(LaneDecisionEntity decision, CancellationToken cancellationToken) => Task.FromResult<bool?>(false);
        }

        private static LinkRecheckService Recheck(ApplicationDbContext db, Func<WordingTierAuditRecord.Record?> record) =>
            new(db, new NoUser(), new LinkAutoApplyService(db, new MemoryCache(new MemoryCacheOptions())), new LineRelinkService(db, new NoUser()),
                new LaneFrameService(db, Array.Empty<ILaneUndo>(), NullLogger<LaneFrameService>.Instance), NullLogger<LinkRecheckService>.Instance, record);

        private static ApplicationDbContext NewContext() =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options);

        private static readonly WordingTierAuditRecord.Record Scored = new()
        {
            Model = "phi4", CheckModel = "mistral-nemo", ScoredAtUtc = DateTime.UtcNow, AuditKnownBad = 400, AuditFlaggedBad = 360, AuditKnownGood = 400, AuditFlaggedGood = 4,
        };

        [Fact]
        public async Task Without_the_record_a_set_stays_in_shadow_for_the_record()
        {
            using var db = NewContext();
            var verdict = await Recheck(db, () => null).AuditAndEvaluateAsync(LinkRecheckRules.Alcohol, Array.Empty<string>(), new Reader("phi4"), new Reader("mistral-nemo"));

            verdict.Mode.Should().Be(LaneMode.Shadow);
            verdict.Reason.Should().Contain("no golden score record");
        }

        [Fact]
        public async Task With_a_record_for_its_auditor_and_reader_b_a_set_waits_only_for_its_audited_sample()
        {
            using var db = NewContext();
            var verdict = await Recheck(db, () => Scored).AuditAndEvaluateAsync(LinkRecheckRules.Alcohol, Array.Empty<string>(), new Reader("phi4"), new Reader("mistral-nemo"));

            verdict.Mode.Should().Be(LaneMode.Shadow);
            verdict.Reason.Should().NotContain("golden score record").And.NotContain("was scored with").And.NotContain(LinkRecheckService.AuditorProblem);
            verdict.Figures.AuditorProblem.Should().BeNull();
        }

        [Fact]
        public async Task A_record_of_another_reader_b_or_no_auditor_keeps_the_set_in_shadow()
        {
            using var db = NewContext();
            (await Recheck(db, () => Scored).AuditAndEvaluateAsync(LinkRecheckRules.Alcohol, Array.Empty<string>(), new Reader("phi4"), new Reader("granite3"))).Reason
                .Should().Contain("reader B mistral-nemo");
            (await Recheck(db, () => Scored).AuditAndEvaluateAsync(LinkRecheckRules.Alcohol, Array.Empty<string>(), null, null)).Reason
                .Should().Contain(LinkRecheckService.AuditorProblem);
        }
    }
}

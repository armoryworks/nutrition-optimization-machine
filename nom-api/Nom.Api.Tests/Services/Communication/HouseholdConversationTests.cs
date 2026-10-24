using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nom.Data;
using Nom.Data.Person;
using Nom.Data.Plan;
using Nom.Orch.Models.Communication;
using Nom.Orch.Services;
using Xunit;

namespace Nom.Api.Tests.Services.Communication
{
    /// <summary>
    /// Compose sends one request: recipients plus the first message. A member of the
    /// sender's household must then see the thread and its message (audit DA-003).
    /// </summary>
    public class HouseholdConversationTests
    {
        private static async Task<ApplicationDbContext> SeedAsync()
        {
            var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            db.Households.AddRange(
                new HouseholdEntity { Id = 1, Name = "Rivera", HouseholdGroupId = 1 },
                new HouseholdEntity { Id = 2, Name = "Other", HouseholdGroupId = 1 });
            db.Persons.AddRange(
                new PersonEntity { Id = 10, Name = "Liz", UserId = "u-liz" },
                new PersonEntity { Id = 11, Name = "Sam", UserId = "u-sam" },
                new PersonEntity { Id = 12, Name = "Diego" },
                new PersonEntity { Id = 13, Name = "Former", UserId = "u-former" },
                new PersonEntity { Id = 20, Name = "Stranger", UserId = "u-x" });
            db.HouseholdMembers.AddRange(
                new HouseholdMemberEntity { HouseholdId = 1, PersonId = 10, Role = "Admin", IsActive = true },
                new HouseholdMemberEntity { HouseholdId = 1, PersonId = 11, Role = "Member", IsActive = true },
                new HouseholdMemberEntity { HouseholdId = 1, PersonId = 12, Role = "Member", IsActive = true },
                new HouseholdMemberEntity { HouseholdId = 1, PersonId = 13, Role = "Member", IsActive = false },
                new HouseholdMemberEntity { HouseholdId = 2, PersonId = 20, Role = "Admin", IsActive = true });
            await db.SaveChangesAsync();
            return db;
        }

        private static CommunicationOrchestrationService Service(ApplicationDbContext db) =>
            new(db, NullLogger<CommunicationOrchestrationService>.Instance);

        [Fact]
        public async Task Recipient_sees_the_thread_and_its_first_message()
        {
            using var db = await SeedAsync();
            var svc = Service(db);

            var threadId = await svc.StartHouseholdConversationAsync(
                new CreateThreadRequest { ParticipantIds = new[] { 11L }, InitialMessage = "  Tacos tonight?  " }, 10);

            var thread = (await svc.GetThreadsAsync(11)).Should().ContainSingle().Subject;
            thread.Id.Should().Be(threadId);
            thread.Participants.Select(p => p.Id).Should().BeEquivalentTo(new[] { 10L, 11L });
            thread.UnreadCount.Should().Be(1);

            var message = (await svc.GetMessagesAsync(threadId, 11)).Should().ContainSingle().Subject;
            message.Content.Should().Be("Tacos tonight?");
            message.SenderPersonId.Should().Be(10);
            message.SenderDisplayName.Should().Be("Liz");
        }

        [Fact]
        public async Task Sending_only_to_yourself_is_rejected()
        {
            using var db = await SeedAsync();

            var act = () => Service(db).StartHouseholdConversationAsync(
                new CreateThreadRequest { ParticipantIds = new[] { 10L }, InitialMessage = "hi" }, 10);

            await act.Should().ThrowAsync<ArgumentException>();
            (await db.MessageThreads.CountAsync()).Should().Be(0);
        }

        [Theory]
        [InlineData(12L)]
        [InlineData(13L)]
        [InlineData(20L)]
        public async Task Dependents_former_members_and_strangers_cannot_be_messaged(long recipientId)
        {
            using var db = await SeedAsync();

            var act = () => Service(db).StartHouseholdConversationAsync(
                new CreateThreadRequest { ParticipantIds = new[] { 11L, recipientId }, InitialMessage = "hi" }, 10);

            await act.Should().ThrowAsync<ArgumentException>();
            (await db.MessageThreads.CountAsync()).Should().Be(0);
        }

        [Fact]
        public async Task Reply_comes_back_as_a_full_message()
        {
            using var db = await SeedAsync();
            var svc = Service(db);
            var threadId = await svc.StartHouseholdConversationAsync(
                new CreateThreadRequest { ParticipantIds = new[] { 11L }, InitialMessage = "Tacos tonight?" }, 10);

            var reply = await svc.PostMessageAsync(new SendMessageRequest { ThreadId = threadId, Content = "Yes!" }, 11);

            reply.Id.Should().BePositive();
            reply.MessageThreadId.Should().Be(threadId);
            reply.SenderDisplayName.Should().Be("Sam");
            reply.Content.Should().Be("Yes!");
            (await svc.GetMessagesAsync(threadId, 10)).Select(m => m.Content).Should().Equal("Tacos tonight?", "Yes!");
        }
    }
}

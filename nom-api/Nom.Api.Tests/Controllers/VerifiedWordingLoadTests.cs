using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Nom.Api.Controllers;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Curation;
using Xunit;

namespace Nom.Api.Tests.Controllers
{
    /// <summary>
    /// A wording batch with no row is refused with a message that names the reason, so a rollout
    /// step that sends the repository's header-only db/verified-wordings.tsv cannot pass as a load of
    /// 0 rows (staging checkpoint 2, M3).
    /// </summary>
    public class VerifiedWordingLoadTests
    {
        private static string RepositoryTable([CallerFilePath] string here = "") =>
            File.ReadAllText(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "..", "db", "verified-wordings.tsv")));

        [Fact]
        public async Task The_repositorys_header_only_table_is_refused_and_the_loader_is_never_called()
        {
            var wordings = new Mock<IVerifiedWordingService>(MockBehavior.Strict);
            var controller = new VerifiedWordingController(wordings.Object);

            var answer = await controller.Load(new VerifiedWordingLoadRequest { Tsv = RepositoryTable() }, "w01", apply: false);

            answer.Result.Should().BeOfType<BadRequestObjectResult>()
                .Which.Value.Should().BeEquivalentTo(new { message = VerifiedWordingController.EmptyBatch });
            VerifiedWordingController.EmptyBatch.Should().Contain("db/verified-wordings.tsv").And.Contain("header only");
        }

        [Fact]
        public async Task A_batch_with_a_row_reaches_the_loader()
        {
            var wordings = new Mock<IVerifiedWordingService>();
            wordings.Setup(w => w.LoadAsync(It.IsAny<IReadOnlyCollection<VerifiedWordingRow>>(), "w01", false, false, false, null))
                .ReturnsAsync(new VerifiedWordingLoadResult { Rows = 1, Added = 1 });
            var controller = new VerifiedWordingController(wordings.Object);
            var tsv = RepositoryTable().TrimEnd('\n') + "\nbaking soda\tfood\t\t175040\tLeavening agents, baking soda\t12\t1\t\n";

            var answer = await controller.Load(new VerifiedWordingLoadRequest { Tsv = tsv }, "w01", apply: false);

            answer.Result.Should().BeOfType<OkObjectResult>();
            wordings.Verify(w => w.LoadAsync(It.Is<IReadOnlyCollection<VerifiedWordingRow>>(r => r.Count == 1), "w01", false, false, false, null), Times.Once);
        }
    }
}

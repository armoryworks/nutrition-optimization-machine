using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nom.Data;
using Nom.Orch;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Curation;
using Xunit;

namespace Nom.Api.Tests.Services
{
    /// <summary>
    /// The lane frame as the container builds it: a lane's undo that needs the lane's own service, which
    /// needs the frame, resolves in the same scope instead of failing the start-up as a circular dependency.
    /// </summary>
    public class LaneFrameRegistrationTests
    {
        private sealed class FrameUndo : ILaneUndo
        {
            public FrameUndo(ILaneFrameService frame) => Frame = frame;

            public ILaneFrameService Frame { get; }

            public string Lane => "frame-undo";

            public Task<LaneUndoResult> UndoSinceAsync(string tier, DateTime sinceUtc, bool apply, CancellationToken cancellationToken) =>
                throw new NotSupportedException();
        }

        [Fact]
        public void An_undo_that_needs_the_frame_resolves_with_the_frame_of_its_scope()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
            services.AddOrchestrationServices();
            services.AddScoped<ILaneUndo, FrameUndo>();

            services.Count(d => d.ServiceType == typeof(ILaneFrameService)).Should().Be(1);
            using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            using var scope = provider.CreateScope();

            var frame = scope.ServiceProvider.GetRequiredService<ILaneFrameService>();
            var undo = scope.ServiceProvider.GetServices<ILaneUndo>().OfType<FrameUndo>().Single();

            undo.Frame.Should().BeSameAs(frame);
        }
    }
}

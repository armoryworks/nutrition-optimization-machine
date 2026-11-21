using Nom.Data;
using Nom.Orch.Interfaces;
using Nom.Orch.Services.Support;

namespace Nom.Api.Services
{
    /// <summary>
    /// The container size lane's nightly hold-out (lane "container-sizes" on the lane frame). Every
    /// ContainerSizes:ScoreEveryHours (default 24) it scores every stored container size against
    /// the sized container lines, including lines imported since the last run, sets each key active,
    /// shadow or paused, records the comparisons on the frame and lets the frame qualify or pause the
    /// lane (a pause undoes the status changes since the last clean window). It uses no model, so
    /// quiet hours and model hosts do not hold it; the switches ai-pause-all and
    /// ai-pause-container-sizes do. Off with ContainerSizes:ScoreEnabled=false; it does nothing
    /// while no size is stored.
    /// </summary>
    public class ContainerSizeHostedService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<ContainerSizeHostedService> _logger;
        private readonly AiLaneGate _gate;

        public ContainerSizeHostedService(IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<ContainerSizeHostedService> logger, AiLaneGate gate)
        {
            _scopeFactory = scopeFactory;
            _configuration = configuration;
            _logger = logger;
            _gate = gate;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_configuration.GetValue("ContainerSizes:ScoreEnabled", true))
            {
                _gate.Disabled(AiLanes.ContainerSizes, "ContainerSizes:ScoreEnabled is false");
                return;
            }
            var every = TimeSpan.FromHours(Math.Max(1, _configuration.GetValue("ContainerSizes:ScoreEveryHours", 24)));
            if (!await Delay(TimeSpan.FromMinutes(10), stoppingToken)) return;

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (!await _gate.PausedAsync(AiLanes.ContainerSizes))
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var sizes = scope.ServiceProvider.GetRequiredService<IContainerSizeService>();
                        var report = await sizes.ScoreAsync(true, SystemConstants.SystemPersonId, null, stoppingToken);
                        _gate.Passed(AiLanes.ContainerSizes, report.DecisionsRecorded, idle: report.Rows == 0);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Container size hold-out failed");
                    _gate.Failed(AiLanes.ContainerSizes, ex);
                }

                if (!await Delay(every, stoppingToken)) return;
            }
        }

        private static async Task<bool> Delay(TimeSpan delay, CancellationToken token)
        {
            try
            {
                await Task.Delay(delay, token);
                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }
    }
}

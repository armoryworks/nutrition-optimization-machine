using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Text.Json;

namespace Nom.Api.Controllers
{
    [ApiController]
    [Route("[controller]")]
    [AllowAnonymous]
    public class HealthController : ControllerBase
    {
        private readonly HealthCheckService _healthCheckService;
        private readonly ILogger<HealthController> _logger;
        private readonly IConfiguration _configuration;

        public HealthController(
            HealthCheckService healthCheckService, 
            ILogger<HealthController> logger,
            IConfiguration configuration)
        {
            _healthCheckService = healthCheckService;
            _logger = logger;
            _configuration = configuration;
        }

        /// <summary>
        /// Main health check endpoint that runs all configured health checks
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Get()
        {
            var report = await _healthCheckService.CheckHealthAsync();

            var result = new
            {
                status = report.Status.ToString(),
                timestamp = DateTime.UtcNow,
                environment = _configuration["ASPNETCORE_ENVIRONMENT"],
                checks = report.Entries.Select(e => new
                {
                    name = e.Key,
                    status = e.Value.Status.ToString(),
                    description = e.Value.Description,
                    duration = e.Value.Duration.TotalMilliseconds,
                    tags = e.Value.Tags,
                    data = e.Value.Data
                })
            };

            var statusCode = report.Status == HealthStatus.Healthy ? 200 : 503;
            
            _logger.LogInformation("Health check completed with status: {Status}", report.Status);
            
            return StatusCode(statusCode, result);
        }

        /// <summary>
        /// Readiness check - indicates if the application is ready to handle requests
        /// </summary>
        [HttpGet("ready")]
        public async Task<IActionResult> Ready()
        {
            var report = await _healthCheckService.CheckHealthAsync(
                predicate: check => check.Tags.Contains("ready"));

            var isReady = report.Status == HealthStatus.Healthy;

            var result = new 
            { 
                status = isReady ? "ready" : "not_ready",
                timestamp = DateTime.UtcNow,
                checks = report.Entries.Select(e => new
                {
                    name = e.Key,
                    status = e.Value.Status.ToString(),
                    duration = e.Value.Duration.TotalMilliseconds
                })
            };

            return isReady ? Ok(result) : StatusCode(503, result);
        }

        /// <summary>
        /// Liveness check - indicates if the application is alive and running
        /// </summary>
        [HttpGet("live")]
        public IActionResult Live()
        {
            // Simple liveness check - if this endpoint responds, the app is alive
            return Ok(new 
            { 
                status = "alive", 
                timestamp = DateTime.UtcNow,
                uptime = GetUptime()
            });
        }

        /// <summary>
        /// Detailed health check endpoint with more verbose information
        /// </summary>
        [HttpGet("detailed")]
        public async Task<IActionResult> Detailed()
        {
            var report = await _healthCheckService.CheckHealthAsync();

            var result = new
            {
                status = report.Status.ToString(),
                timestamp = DateTime.UtcNow,
                environment = _configuration["ASPNETCORE_ENVIRONMENT"],
                version = GetType().Assembly.GetName().Version?.ToString(),
                uptime = GetUptime(),
                totalDuration = report.TotalDuration.TotalMilliseconds,
                checks = report.Entries.Select(e => new
                {
                    name = e.Key,
                    status = e.Value.Status.ToString(),
                    description = e.Value.Description,
                    duration = e.Value.Duration.TotalMilliseconds,
                    tags = e.Value.Tags,
                    exception = e.Value.Exception?.Message,
                    data = e.Value.Data
                }).OrderBy(c => c.name)
            };

            var statusCode = report.Status == HealthStatus.Healthy ? 200 : 503;
            return StatusCode(statusCode, result);
        }

        private string GetUptime()
        {
            var uptime = DateTime.UtcNow - System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime();
            return $"{(int)uptime.TotalDays}d {uptime.Hours}h {uptime.Minutes}m {uptime.Seconds}s";
        }
    }
}
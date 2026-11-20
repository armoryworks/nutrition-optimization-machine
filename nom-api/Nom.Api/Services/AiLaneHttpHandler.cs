using System.Diagnostics;
using System.Net;
using System.Text.Json;

namespace Nom.Api.Services
{
    /// <summary>
    /// Sits in front of every model client a lane uses: refuses a call at once while its host is
    /// marked down, and reports each call's host, model, outcome and latency to <see cref="AiHostMonitor"/>.
    /// </summary>
    public sealed class AiLaneHttpHandler : DelegatingHandler
    {
        private readonly AiHostMonitor _monitor;
        private readonly string _lane;

        public AiLaneHttpHandler(AiHostMonitor monitor, string lane)
        {
            _monitor = monitor;
            _lane = lane;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var host = request.RequestUri?.GetLeftPart(UriPartial.Authority) ?? string.Empty;
            if (_monitor.IsDown(host))
            {
                throw new HttpRequestException($"AI host {host} is marked down; the call waits for its next health probe");
            }

            var model = await ModelOfAsync(request, cancellationToken);
            var watch = Stopwatch.StartNew();
            try
            {
                var response = await base.SendAsync(request, cancellationToken);
                var missing = response.StatusCode == HttpStatusCode.NotFound;
                var healthy = (int)response.StatusCode < 500 && !missing;
                _monitor.Record(_lane, host, model, healthy, response.IsSuccessStatusCode ? null : $"HTTP {(int)response.StatusCode}", watch.Elapsed, modelMissing: missing);
                return response;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                _monitor.Record(_lane, host, model, false, ex.GetType().Name + ": " + ex.Message, watch.Elapsed);
                throw;
            }
        }

        private static async Task<string?> ModelOfAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content == null) return null;
            try
            {
                using var body = JsonDocument.Parse(await request.Content.ReadAsStringAsync(cancellationToken));
                return body.RootElement.ValueKind == JsonValueKind.Object
                    && body.RootElement.TryGetProperty("model", out var model)
                    && model.ValueKind == JsonValueKind.String
                    ? model.GetString()
                    : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}

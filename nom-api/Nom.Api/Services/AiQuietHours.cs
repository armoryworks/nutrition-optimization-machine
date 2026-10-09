namespace Nom.Api.Services
{
    /// <summary>
    /// A nightly window in which the API's local-model lanes leave the GPU alone, so batch jobs
    /// outside the API (the recipe image finder) can load their own models. Ai:QuietHours as
    /// "HH:mm-HH:mm" (default 01:00-05:00, may wrap midnight; empty disables) in
    /// Ai:QuietHoursTimeZone (default America/Denver).
    /// </summary>
    public static class AiQuietHours
    {
        public static TimeSpan? Remaining(IConfiguration configuration, DateTimeOffset now)
        {
            var window = configuration.GetValue("Ai:QuietHours", "01:00-05:00");
            if (string.IsNullOrWhiteSpace(window)) return null;
            var parts = window.Split('-', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || !TimeOnly.TryParse(parts[0], out var start) || !TimeOnly.TryParse(parts[1], out var end) || start == end) return null;

            TimeZoneInfo zone;
            try { zone = TimeZoneInfo.FindSystemTimeZoneById(configuration.GetValue("Ai:QuietHoursTimeZone", "America/Denver")!); }
            catch (TimeZoneNotFoundException) { zone = TimeZoneInfo.Utc; }

            var local = TimeZoneInfo.ConvertTime(now, zone);
            var time = TimeOnly.FromDateTime(local.DateTime);
            if (!time.IsBetween(start, end)) return null;
            var left = end - time;
            return left <= TimeSpan.Zero ? null : left;
        }

        /// <summary>Waits out the quiet window if it is in effect; false when cancelled.</summary>
        public static async Task<bool> WaitAsync(IConfiguration configuration, ILogger logger, CancellationToken token)
        {
            if (Remaining(configuration, DateTimeOffset.UtcNow) is not { } left) return true;
            logger.LogInformation("AI quiet hours: pausing for {Minutes:0} min to leave the GPU to batch jobs", left.TotalMinutes);
            try
            {
                await Task.Delay(left, token);
                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }
    }
}

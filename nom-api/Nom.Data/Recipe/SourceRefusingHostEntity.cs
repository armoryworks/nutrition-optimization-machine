using System;

namespace Nom.Data.Recipe
{
    /// <summary>
    /// A site that refused a discovery run (the scraper's <c>refusingHosts</c>: a bot challenge or a 429 on any page, or a 401,
    /// 402 or 403 on its robots.txt or homepage). It is held from every later discovery run, on the exclusion list as
    /// permission-needed and never sent as a seed, until an admin has looked at it (<see cref="ReleasedAtUtc"/>); a refusal
    /// reported after that holds it again.
    /// </summary>
    public class SourceRefusingHostEntity : BaseEntity
    {
        /// <summary>Host name, lowercased, without a leading "www.".</summary>
        public string Host { get; set; } = string.Empty;

        public DateTime FirstRefusedUtc { get; set; }

        public DateTime LastRefusedUtc { get; set; }

        public int TimesRefused { get; set; }

        /// <summary>The page that refused, as the scraper reported it.</summary>
        public string? LastUrl { get; set; }

        public int? LastHttpStatus { get; set; }

        public bool LastBotChallenge { get; set; }

        /// <summary>The discovery run that last reported the refusal.</summary>
        public long? LastRunId { get; set; }

        /// <summary>When an admin looked at the refusal and released the host for discovery; null while it is held.</summary>
        public DateTime? ReleasedAtUtc { get; set; }

        public long? ReleasedByPersonId { get; set; }

        public string? ReleasedNote { get; set; }
    }
}

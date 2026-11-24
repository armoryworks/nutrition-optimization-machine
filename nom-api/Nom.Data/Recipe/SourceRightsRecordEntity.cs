using System;

namespace Nom.Data.Recipe
{
    /// <summary>
    /// A rights class an admin recorded by hand for a host (a scraping source's or a candidate's, or a host NOM has no row
    /// for yet), with the site's own deciding words, the page they stand on, the date they were read and who recorded it. It
    /// is how a host whose terms already forbid automated access is classed without a request to it. While it stands
    /// (<see cref="WithdrawnAtUtc"/> null) the class holds every source and candidate of the host: the source lane never
    /// overwrites it, and a forbidden or permission-needed host is never fetched or checked and is on every discovery job's
    /// exclusion list. Withdrawn by undoing its journal entry; never set by a model.
    /// </summary>
    public class SourceRightsRecordEntity : BaseEntity
    {
        /// <summary>Host name, lowercased, without a leading "www." (it covers the bare and the www. row of a site).</summary>
        public string Host { get; set; } = string.Empty;

        /// <summary><c>facts-only</c>, <c>permission-needed</c> or <c>forbidden</c>; <c>clear</c> only beside a recorded written permission.</summary>
        public string RightsClass { get; set; } = string.Empty;

        /// <summary>The site's own deciding words, quoted.</summary>
        public string Quote { get; set; } = string.Empty;

        /// <summary>The page the words stand on.</summary>
        public string QuoteUrl { get; set; } = string.Empty;

        /// <summary>The date the admin read the words on that page.</summary>
        public DateTime ReadOn { get; set; }

        public long RecordedByPersonId { get; set; }

        public DateTime RecordedAtUtc { get; set; }

        /// <summary>While set, the record no longer holds (its journal entry was undone).</summary>
        public DateTime? WithdrawnAtUtc { get; set; }

        public long? WithdrawnByPersonId { get; set; }
    }
}

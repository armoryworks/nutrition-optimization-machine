// File: nom-api/Nom.Data/Recipe/ScrapingSourceEntity.cs

using System;
using Nom.Data.Person;

namespace Nom.Data.Recipe
{
    public enum ScrapingSourceStatusEnum
    {
        Pending = 0,
        Approved = 1,
        Rejected = 2,
    }

    /// <summary>
    /// A domain that may be scraped. Scraping is whitelist-only: no URL is ever
    /// fetched unless an admin has approved its domain here. When a user submits
    /// a URL from an unknown domain, a Pending row is created and admins are
    /// notified in-app and by email; approving it is an explicit acceptance of
    /// responsibility for the legality and quality of that source.
    /// </summary>
    public class ScrapingSourceEntity : BaseEntity
    {
        /// <summary>Host name, lowercased, no scheme (e.g. "www.example.com").</summary>
        public string Domain { get; set; } = string.Empty;

        public ScrapingSourceStatusEnum Status { get; set; } = ScrapingSourceStatusEnum.Pending;

        /// <summary>The URL whose submission created this request — lets the reviewer inspect the site.</summary>
        public string? SampleUrl { get; set; }

        public long? RequestedByPersonId { get; set; }
        public virtual PersonEntity? RequestedByPerson { get; set; }

        public long? ReviewedByPersonId { get; set; }
        public virtual PersonEntity? ReviewedByPerson { get; set; }

        public DateTime? ReviewedDate { get; set; }

        /// <summary>Reviewer notes — reason for rejection, licensing observations, etc.</summary>
        public string? Notes { get; set; }
    }
}

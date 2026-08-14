using System.Collections.Generic;
using System.Threading.Tasks;
using Nom.Data.Recipe;
using Nom.Orch.Models.Recipe;

namespace Nom.Orch.Interfaces
{
    /// <summary>
    /// Scraping-source whitelist. Scraping is deny-by-default: only domains an
    /// admin has explicitly approved are ever fetched. Approving a source is the
    /// admin accepting responsibility for the legality and quality of scraping it.
    /// </summary>
    public interface IScrapingSourceService
    {
        /// <summary>Whitelist status of a URL's domain; null when the domain has never been requested.</summary>
        Task<ScrapingSourceStatusEnum?> GetDomainStatusAsync(string url);

        /// <summary>
        /// Registers a Pending request for the URL's domain (idempotent) and, when
        /// newly created, notifies all curation admins in-app and by email.
        /// </summary>
        Task<ScrapingSourceModel> RequestSourceAsync(string url, long? requestedByPersonId, string? note = null);

        /// <summary>
        /// Registers the URL's domain as an already-Approved source with automated
        /// (system) provenance — used by source discovery when a candidate passes
        /// the clean-probe gate. Idempotent: an existing row for the domain is
        /// returned untouched, so a human decision (including Rejected) is never
        /// overridden. Admins are notified that the domain was auto-whitelisted
        /// and where to revoke it.
        /// </summary>
        Task<ScrapingSourceModel> RegisterAutoApprovedSourceAsync(string url, string reason);

        Task<List<ScrapingSourceModel>> ListAsync(ScrapingSourceStatusEnum? status);

        Task<ScrapingSourceModel?> ApproveAsync(long id, long reviewerPersonId, string? notes);

        Task<ScrapingSourceModel?> RejectAsync(long id, long reviewerPersonId, string? notes);
    }
}

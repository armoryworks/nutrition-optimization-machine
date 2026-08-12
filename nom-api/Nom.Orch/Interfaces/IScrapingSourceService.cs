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
        Task<ScrapingSourceModel> RequestSourceAsync(string url, long? requestedByPersonId);

        Task<List<ScrapingSourceModel>> ListAsync(ScrapingSourceStatusEnum? status);

        Task<ScrapingSourceModel?> ApproveAsync(long id, long reviewerPersonId, string? notes);

        Task<ScrapingSourceModel?> RejectAsync(long id, long reviewerPersonId, string? notes);
    }
}

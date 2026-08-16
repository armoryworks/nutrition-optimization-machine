using System.Threading.Tasks;
using Nom.Orch.Models.Curation;

namespace Nom.Orch.Interfaces
{
    /// <summary>
    /// Deterministic quality audit of the ingredient catalog — no model, no network. Run this
    /// before spending tokens on any automated review; it catches most real problems for free.
    /// </summary>
    public interface IFoodCatalogAuditService
    {
        /// <param name="source">Optional FdcDataType filter ("foundation_food", "branded_food").</param>
        Task<FoodCatalogAuditResult> AuditAsync(string? source = null, int limit = 5000);
    }
}

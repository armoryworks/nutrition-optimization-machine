// File: Nom.Orch/Services/DataExportOrchestrationService.cs

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nom.Data;
using Nom.Orch.Interfaces;
using System.Linq;
using System.Threading.Tasks;

namespace Nom.Orch.Services
{
    /// <summary>
    /// Implements the logic for exporting user data.
    /// </summary>
    public class DataExportOrchestrationService : IDataExportOrchestrationService
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly ILogger<DataExportOrchestrationService> _logger;

        public DataExportOrchestrationService(ApplicationDbContext dbContext, ILogger<DataExportOrchestrationService> logger)
        {
            _dbContext = dbContext;
            _logger = logger;
        }

        public async Task ExportPersonDataAsync(long personId, string format)
        {
            var personData = await _dbContext.Persons
                .AsNoTracking()
                .Where(p => p.Id == personId)
                .Select(p => new
                {
                    p.Id,
                    p.Name,
                    p.UserId,
                    Attributes = p.Attributes.Select(a => new { a.AttributeType.Name, a.Value }),
                    Restrictions = p.Restrictions.Select(r => new { r.Name, r.Description })
                })
                .FirstOrDefaultAsync();

            if (personData == null)
            {
                _logger.LogWarning("Person with ID {PersonId} not found for data export.", personId);
                return;
            }

            // Deliberately NOT logged. This projection carries Restrictions - allergies,
            // intolerances, medically motivated diets - which is health-adjacent data, and the
            // privacy policy describes operational logs as holding request metadata only.
            // Writing the payload here made pressing "Export" the thing that leaked it.
            //
            // The export itself is not implemented here: the request is recorded in
            // PrivacyRequests and fulfilled from there. Deliver it from the record, never
            // from a log scrape.
        }
    }
}

// File: Nom.Orch/Services/DataExportOrchestrationService.cs

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nom.Data;
using Nom.Orch.Interfaces;
using System.Linq;
using System.Text.Json;
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

            var jsonData = JsonSerializer.Serialize(personData, new JsonSerializerOptions { WriteIndented = true });

            _logger.LogInformation("--- DATA EXPORT FOR PERSON ID: {PersonId} ---", personId);
            _logger.LogInformation("{JsonData}", jsonData);
            _logger.LogInformation("--- END OF DATA EXPORT ---");
        }
    }
}

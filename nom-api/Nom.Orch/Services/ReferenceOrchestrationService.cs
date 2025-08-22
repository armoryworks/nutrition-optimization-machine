// File: Nom.Orch/Services/ReferenceOrchestrationService.cs

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nom.Data;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Reference;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Nom.Orch.Services
{
    public class ReferenceOrchestrationService : IReferenceOrchestrationService
    {
        private readonly ApplicationDbContext _db;
        private readonly ILogger<ReferenceOrchestrationService> _logger;

        public ReferenceOrchestrationService(ApplicationDbContext db, ILogger<ReferenceOrchestrationService> logger)
        {
            _db = db;
            _logger = logger;
        }

        /// <summary>
        /// Gets all references for a specific reference group.
        /// </summary>
        /// <param name="discriminatorId">The discriminator ID for the reference group</param>
        /// <returns>List of references for the specified group</returns>
        public async Task<List<Nom.Data.Reference.GroupedReferenceViewEntity>> GetReferencesByGroupAsync(long discriminatorId)
        {
            try
            {
                var references = await _db.Set<Nom.Data.Reference.GroupedReferenceViewEntity>()
                    .Where(r => r.GroupId == discriminatorId)
                    .OrderBy(r => r.ReferenceName)
                    .ToListAsync();

                return references;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving references for group {DiscriminatorId}", discriminatorId);
                throw;
            }
        }

        /// <summary>
        /// Gets multiple reference groups in one call for performance.
        /// </summary>
        /// <param name="discriminatorIds">Array of discriminator IDs</param>
        /// <returns>Dictionary of discriminator ID to references mapping</returns>
        public async Task<Dictionary<long, List<Nom.Data.Reference.GroupedReferenceViewEntity>>> GetReferencesBulkAsync(long[] discriminatorIds)
        {
            try
            {
                var references = await _db.Set<Nom.Data.Reference.GroupedReferenceViewEntity>()
                    .Where(r => discriminatorIds.Contains(r.GroupId))
                    .OrderBy(r => r.GroupId)
                    .ThenBy(r => r.ReferenceName)
                    .ToListAsync();

                var result = new Dictionary<long, List<Nom.Data.Reference.GroupedReferenceViewEntity>>();
                
                foreach (var discriminatorId in discriminatorIds)
                {
                    result[discriminatorId] = references
                        .Where(r => r.GroupId == discriminatorId)
                        .ToList();
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving references in bulk for groups {DiscriminatorIds}", string.Join(",", discriminatorIds));
                throw;
            }
        }
    }
}
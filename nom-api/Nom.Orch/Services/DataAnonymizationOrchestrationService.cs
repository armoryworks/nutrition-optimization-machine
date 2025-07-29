// File: Nom.Orch/Services/DataAnonymizationOrchestrationService.cs

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nom.Data;
using Nom.Orch.Interfaces;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Nom.Orch.Services
{
    /// <summary>
    /// Implements the logic for anonymizing user data upon request.
    /// </summary>
    public class DataAnonymizationOrchestrationService : IDataAnonymizationOrchestrationService
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly ILogger<DataAnonymizationOrchestrationService> _logger;

        public DataAnonymizationOrchestrationService(ApplicationDbContext dbContext, UserManager<IdentityUser> userManager, ILogger<DataAnonymizationOrchestrationService> logger)
        {
            _dbContext = dbContext;
            _userManager = userManager;
            _logger = logger;
        }

        public async Task AnonymizePersonDataAsync(long personId)
        {
            var person = await _dbContext.Persons.FindAsync(personId);
            if (person == null)
            {
                _logger.LogWarning("Person with ID {PersonId} not found for anonymization.", personId);
                return;
            }

            using var transaction = await _dbContext.Database.BeginTransactionAsync();
            try
            {
                person.Name = $"Anonymized_{person.Id}";
                var identityUserId = person.UserId;
                person.UserId = null;
                // InvitationCode is now handled by separate InvitationEntity

                var attributes = _dbContext.PersonAttributes.Where(pa => pa.PersonId == personId);
                _dbContext.PersonAttributes.RemoveRange(attributes);

                var restrictions = _dbContext.Restrictions.Where(r => r.PersonId == personId);
                _dbContext.Restrictions.RemoveRange(restrictions);

                if (!string.IsNullOrEmpty(identityUserId))
                {
                    var identityUser = await _userManager.FindByIdAsync(identityUserId);
                    if (identityUser != null)
                    {
                        var result = await _userManager.DeleteAsync(identityUser);
                        if (!result.Succeeded)
                        {
                            _logger.LogWarning("Failed to delete identity user {UserId}: {Errors}", identityUserId, string.Join(", ", result.Errors.Select(e => e.Description)));
                        }
                    }
                }

                _dbContext.Persons.Update(person);
                await _dbContext.SaveChangesAsync();
                await transaction.CommitAsync();
                _logger.LogInformation("Successfully anonymized data for PersonId {PersonId}", personId);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Failed to anonymize data for PersonId {PersonId}", personId);
            }
        }
    }
}

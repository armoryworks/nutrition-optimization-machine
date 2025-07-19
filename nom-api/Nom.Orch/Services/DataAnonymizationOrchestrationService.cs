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
                person.Name = $"[Deleted User {person.Id}]";
                person.InvitationCode = null;
                var identityUserId = person.UserId;
                person.UserId = null;

                var attributes = _dbContext.PersonAttributes.Where(pa => pa.PersonId == personId);
                _dbContext.PersonAttributes.RemoveRange(attributes);

                var restrictions = _dbContext.Restrictions.Where(r => r.PersonId == personId);
                _dbContext.Restrictions.RemoveRange(restrictions);

                if (!string.IsNullOrEmpty(identityUserId))
                {
                    var identityUser = await _userManager.FindByIdAsync(identityUserId);
                    if (identityUser != null)
                    {
                        identityUser.Email = $"{person.Id}@deleted.user";
                        identityUser.NormalizedEmail = identityUser.Email.ToUpperInvariant();
                        identityUser.UserName = identityUser.Email;
                        identityUser.NormalizedUserName = identityUser.NormalizedEmail;
                        identityUser.PasswordHash = null;
                        identityUser.SecurityStamp = Guid.NewGuid().ToString();
                        identityUser.EmailConfirmed = false;
                        identityUser.PhoneNumber = null;
                        identityUser.PhoneNumberConfirmed = false;
                        identityUser.TwoFactorEnabled = false;
                        await _userManager.UpdateAsync(identityUser);
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

// Nom.Orch/Services/PersonOrchestrationService.cs
using Nom.Orch.Interfaces;
using Nom.Data; // For ApplicationDbContext
using Nom.Data.Person; // For PersonEntity
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore; // For ToListAsync() and FirstOrDefaultAsync()

namespace Nom.Orch.Services
{
    /// <summary>
    /// Implements the business logic for Person orchestration,
    /// managing the lifecycle and initial setup of Person entities.
    /// </summary>
    public class PersonOrchestrationService : IPersonOrchestrationService
    {
        private readonly ApplicationDbContext _dbContext;

        public PersonOrchestrationService(ApplicationDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        /// <summary>
        /// Sets up a new Person entity after successful user registration.
        /// Creates a Person record, generates an invitation code, and saves to the database.
        /// </summary>
        /// <param name="identityUserId">The ID of the IdentityUser linked to this person.</param>
        /// <param name="personName">The initial name for the person.</param>
        /// <returns>The newly created PersonEntity.</returns>
        public async Task<PersonEntity> SetupNewRegisteredPersonAsync(string identityUserId, string personName)
        {
            // Ensure the "System" person exists
            var systemPerson = await _dbContext.Persons.FirstOrDefaultAsync(p => p.Id == 1L);
            if (systemPerson == null)
            {
                systemPerson = new PersonEntity
                {
                    Id = 1L,
                    Name = "System",
                    UserId = null,
                    InvitationCode = null
                };
                _dbContext.Persons.Add(systemPerson);
                await _dbContext.SaveChangesAsync();
            }

            // Generate a unique invitation code for the new person
            var invitationCode = await GenerateUniqueInvitationCodeAsync();

            var newPerson = new PersonEntity
            {
                Name = personName,
                UserId = identityUserId,
                InvitationCode = invitationCode
            };

            // Add the new person to the database
            _dbContext.Persons.Add(newPerson);
            await _dbContext.SaveChangesAsync();

            return newPerson;
        }

        /// <summary>
        /// Generates a unique 6-character alphanumeric invitation code.
        /// </summary>
        /// <returns>A unique invitation code string.</returns>
        public async Task<string> GenerateUniqueInvitationCodeAsync()
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            var random = new Random();
            string code;
            bool isUnique;

            do
            {
                code = new string(Enumerable.Repeat(chars, 6)
                  .Select(s => s[random.Next(s.Length)]).ToArray());

                // Check if the code already exists in the database
                isUnique = !await _dbContext.Persons.AnyAsync(p => p.InvitationCode == code);

            } while (!isUnique);

            return code;
        }
    }
}
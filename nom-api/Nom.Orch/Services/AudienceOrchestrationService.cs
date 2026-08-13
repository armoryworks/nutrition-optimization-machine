using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;

using Nom.Data;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;

namespace Nom.Orch.Services
{
    public class AudienceOrchestrationService : IAudienceOrchestrationService
    {
        private readonly ApplicationDbContext _context;

        public AudienceOrchestrationService(ApplicationDbContext context)
        {
            _context = context;
        }

        public Task<List<AudienceModel>> GetMineAsync(long personId) =>
            _context.Audiences
                .AsNoTracking()
                .Where(a => a.OwnerPersonId == personId)
                .Select(a => new AudienceModel
                {
                    Id = a.Id,
                    Name = a.Name,
                    OwnerPersonId = a.OwnerPersonId,
                    ManagedBy = a.ManagedBy,
                    HouseholdCount = a.Members.Count,
                    RecipeCount = a.Recipes.Count,
                })
                .ToListAsync();

        public async Task<AudienceModel> CreateAsync(string name, long ownerPersonId)
        {
            var entity = new AudienceEntity
            {
                Name = name.Trim(),
                OwnerPersonId = ownerPersonId,
                CreatedDate = DateTime.UtcNow,
                CreatedByPersonId = ownerPersonId,
            };
            _context.Audiences.Add(entity);
            await _context.SaveChangesAsync();
            return new AudienceModel { Id = entity.Id, Name = entity.Name, OwnerPersonId = ownerPersonId };
        }

        public async Task<bool> DeleteAsync(long audienceId, long requesterPersonId)
        {
            var audience = await GetOwnedMutableAsync(audienceId, requesterPersonId);
            if (audience == null) return false;

            // Scoped recipes lose their audience link; their visibility stays
            // Audience (unreachable) until the author re-publishes — safer than
            // silently going public.
            _context.Audiences.Remove(audience);
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> AddHouseholdAsync(long audienceId, long householdId, long requesterPersonId)
        {
            var audience = await GetOwnedMutableAsync(audienceId, requesterPersonId);
            if (audience == null) return false;

            var exists = await _context.AudienceMembers
                .AnyAsync(am => am.AudienceId == audienceId && am.HouseholdId == householdId);
            if (!exists)
            {
                _context.AudienceMembers.Add(new AudienceMemberEntity
                {
                    AudienceId = audienceId,
                    HouseholdId = householdId,
                    CreatedDate = DateTime.UtcNow,
                    CreatedByPersonId = requesterPersonId,
                });
                await _context.SaveChangesAsync();
            }
            return true;
        }

        public async Task<bool> RemoveHouseholdAsync(long audienceId, long householdId, long requesterPersonId)
        {
            var audience = await GetOwnedMutableAsync(audienceId, requesterPersonId);
            if (audience == null) return false;

            var member = await _context.AudienceMembers
                .FirstOrDefaultAsync(am => am.AudienceId == audienceId && am.HouseholdId == householdId);
            if (member != null)
            {
                _context.AudienceMembers.Remove(member);
                await _context.SaveChangesAsync();
            }
            return true;
        }

        public async Task<bool> AttachRecipeAsync(long audienceId, long recipeId, long requesterPersonId)
        {
            var audience = await GetOwnedMutableAsync(audienceId, requesterPersonId);
            if (audience == null) return false;

            // Only the recipe's author scopes it to an audience.
            var authored = await _context.Recipes
                .AnyAsync(r => r.Id == recipeId && r.AuthorId == requesterPersonId);
            if (!authored)
            {
                throw new UnauthorizedAccessException("Only the recipe author can scope a recipe to an audience.");
            }

            var exists = await _context.RecipeAudiences
                .AnyAsync(ra => ra.AudienceId == audienceId && ra.RecipeId == recipeId);
            if (!exists)
            {
                _context.RecipeAudiences.Add(new RecipeAudienceEntity
                {
                    AudienceId = audienceId,
                    RecipeId = recipeId,
                    CreatedDate = DateTime.UtcNow,
                    CreatedByPersonId = requesterPersonId,
                });
                await _context.SaveChangesAsync();
            }
            return true;
        }

        public async Task<bool> DetachRecipeAsync(long audienceId, long recipeId, long requesterPersonId)
        {
            var audience = await GetOwnedMutableAsync(audienceId, requesterPersonId);
            if (audience == null) return false;

            var link = await _context.RecipeAudiences
                .FirstOrDefaultAsync(ra => ra.AudienceId == audienceId && ra.RecipeId == recipeId);
            if (link != null)
            {
                _context.RecipeAudiences.Remove(link);
                await _context.SaveChangesAsync();
            }
            return true;
        }

        /// <summary>Owned by the requester and not maintained by an external manager.</summary>
        private async Task<AudienceEntity?> GetOwnedMutableAsync(long audienceId, long requesterPersonId)
        {
            var audience = await _context.Audiences
                .FirstOrDefaultAsync(a => a.Id == audienceId && a.OwnerPersonId == requesterPersonId);
            if (audience == null) return null;

            if (!string.IsNullOrEmpty(audience.ManagedBy) && !audience.ManagedBy.StartsWith("person:"))
            {
                throw new UnauthorizedAccessException("This audience is maintained by an external manager.");
            }
            return audience;
        }
    }
}

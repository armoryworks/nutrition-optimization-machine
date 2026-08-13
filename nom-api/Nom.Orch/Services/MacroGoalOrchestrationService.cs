using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nom.Data;
using Nom.Data.Plan;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Plan;

namespace Nom.Orch.Services
{
    public class MacroGoalOrchestrationService : IMacroGoalOrchestrationService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<MacroGoalOrchestrationService> _logger;

        public MacroGoalOrchestrationService(
            ApplicationDbContext context,
            ILogger<MacroGoalOrchestrationService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<MacroGoalModel?> GetPersonGoalAsync(long personId)
        {
            var entity = await _context.MacroGoals
                .AsNoTracking()
                .FirstOrDefaultAsync(g => g.PersonId == personId);
            return entity == null ? null : ToModel(entity);
        }

        public async Task<MacroGoalModel> SavePersonGoalAsync(long personId, MacroGoalModel model)
        {
            var entity = await _context.MacroGoals
                .FirstOrDefaultAsync(g => g.PersonId == personId);

            if (entity == null)
            {
                entity = new MacroGoalEntity { PersonId = personId };
                _context.MacroGoals.Add(entity);
            }

            Apply(entity, model);
            await _context.SaveChangesAsync();
            _logger.LogInformation("Saved macro goals for person {PersonId}", personId);
            return ToModel(entity);
        }

        public async Task<MacroGoalModel?> GetHouseholdGoalAsync(long householdId)
        {
            var entity = await _context.MacroGoals
                .AsNoTracking()
                .FirstOrDefaultAsync(g => g.HouseholdId == householdId);
            return entity == null ? null : ToModel(entity);
        }

        public async Task<MacroGoalModel> SaveHouseholdGoalAsync(long householdId, MacroGoalModel model)
        {
            var entity = await _context.MacroGoals
                .FirstOrDefaultAsync(g => g.HouseholdId == householdId);

            if (entity == null)
            {
                entity = new MacroGoalEntity { HouseholdId = householdId };
                _context.MacroGoals.Add(entity);
            }

            Apply(entity, model);
            await _context.SaveChangesAsync();
            _logger.LogInformation("Saved macro goals for household {HouseholdId}", householdId);
            return ToModel(entity);
        }

        public async Task<EffectiveMacroGoalModel> GetEffectiveForPersonAsync(long personId)
        {
            var own = await GetPersonGoalAsync(personId);
            if (own?.HasAnyTarget == true)
            {
                return ToEffective(own, "person");
            }

            // Fall back to the person's active household(s); use the first
            // household that has a goal set.
            var householdIds = await _context.HouseholdMembers
                .AsNoTracking()
                .Where(hm => hm.PersonId == personId && hm.IsActive)
                .OrderBy(hm => hm.JoinedDate)
                .Select(hm => hm.HouseholdId)
                .ToListAsync();

            foreach (var householdId in householdIds)
            {
                var household = await GetHouseholdGoalAsync(householdId);
                if (household?.HasAnyTarget == true)
                {
                    return ToEffective(household, "household");
                }
            }

            return new EffectiveMacroGoalModel { Source = "none" };
        }

        public Task<MacroGoalModel?> GetEffectiveForHouseholdAsync(long householdId)
            => GetHouseholdGoalAsync(householdId);

        private static void Apply(MacroGoalEntity entity, MacroGoalModel model)
        {
            entity.CaloriesTarget = model.CaloriesTarget;
            entity.ProteinGramsTarget = model.ProteinGramsTarget;
            entity.CarbGramsTarget = model.CarbGramsTarget;
            entity.FatGramsTarget = model.FatGramsTarget;
        }

        private static MacroGoalModel ToModel(MacroGoalEntity entity) => new()
        {
            CaloriesTarget = entity.CaloriesTarget,
            ProteinGramsTarget = entity.ProteinGramsTarget,
            CarbGramsTarget = entity.CarbGramsTarget,
            FatGramsTarget = entity.FatGramsTarget,
        };

        private static EffectiveMacroGoalModel ToEffective(MacroGoalModel model, string source) => new()
        {
            CaloriesTarget = model.CaloriesTarget,
            ProteinGramsTarget = model.ProteinGramsTarget,
            CarbGramsTarget = model.CarbGramsTarget,
            FatGramsTarget = model.FatGramsTarget,
            Source = source,
        };
    }
}

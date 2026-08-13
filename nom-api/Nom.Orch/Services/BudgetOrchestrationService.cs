using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nom.Data;
using Nom.Data.Plan;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Plan;

namespace Nom.Orch.Services
{
    public class BudgetOrchestrationService : IBudgetOrchestrationService
    {
        private static readonly string[] ValidPeriods = { "weekly", "monthly" };

        private readonly ApplicationDbContext _context;
        private readonly ILogger<BudgetOrchestrationService> _logger;

        public BudgetOrchestrationService(ApplicationDbContext context, ILogger<BudgetOrchestrationService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<BudgetModel?> GetPersonBudgetAsync(long personId)
        {
            var e = await _context.Budgets.AsNoTracking().FirstOrDefaultAsync(b => b.PersonId == personId);
            return e == null ? null : ToModel(e);
        }

        public Task<BudgetModel> SavePersonBudgetAsync(long personId, BudgetModel model) =>
            SaveAsync(b => b.PersonId == personId, () => new BudgetEntity { PersonId = personId }, model, $"person {personId}");

        public async Task<BudgetModel?> GetHouseholdBudgetAsync(long householdId)
        {
            var e = await _context.Budgets.AsNoTracking().FirstOrDefaultAsync(b => b.HouseholdId == householdId);
            return e == null ? null : ToModel(e);
        }

        public Task<BudgetModel> SaveHouseholdBudgetAsync(long householdId, BudgetModel model) =>
            SaveAsync(b => b.HouseholdId == householdId, () => new BudgetEntity { HouseholdId = householdId }, model, $"household {householdId}");

        public async Task<EffectiveBudgetModel> GetEffectiveForPersonAsync(long personId)
        {
            var own = await GetPersonBudgetAsync(personId);
            if (own != null) return ToEffective(own, "person");

            var householdIds = await _context.HouseholdMembers
                .AsNoTracking()
                .Where(hm => hm.PersonId == personId && hm.IsActive)
                .OrderBy(hm => hm.JoinedDate)
                .Select(hm => hm.HouseholdId)
                .ToListAsync();

            foreach (var hid in householdIds)
            {
                var hb = await GetHouseholdBudgetAsync(hid);
                if (hb != null) return ToEffective(hb, "household");
            }

            return new EffectiveBudgetModel { Source = "none", HasBudget = false };
        }

        private async Task<BudgetModel> SaveAsync(
            System.Linq.Expressions.Expression<System.Func<BudgetEntity, bool>> match,
            System.Func<BudgetEntity> create, BudgetModel model, string who)
        {
            if (model.Amount < 0)
                throw new System.ArgumentException("Budget amount cannot be negative.");
            var period = (model.Period ?? "weekly").ToLowerInvariant();
            if (!ValidPeriods.Contains(period))
                throw new System.ArgumentException("Budget period must be 'weekly' or 'monthly'.");

            var entity = await _context.Budgets.FirstOrDefaultAsync(match);
            if (entity == null)
            {
                entity = create();
                _context.Budgets.Add(entity);
            }
            entity.Amount = model.Amount;
            entity.Currency = string.IsNullOrWhiteSpace(model.Currency) ? "USD" : model.Currency.ToUpperInvariant();
            entity.Period = period;
            await _context.SaveChangesAsync();
            _logger.LogInformation("Saved budget for {Who}", who);
            return ToModel(entity);
        }

        private static BudgetModel ToModel(BudgetEntity e) => new()
        {
            Amount = e.Amount,
            Currency = e.Currency,
            Period = e.Period,
        };

        private static EffectiveBudgetModel ToEffective(BudgetModel m, string source) => new()
        {
            Amount = m.Amount,
            Currency = m.Currency,
            Period = m.Period,
            Source = source,
            HasBudget = true,
        };
    }
}

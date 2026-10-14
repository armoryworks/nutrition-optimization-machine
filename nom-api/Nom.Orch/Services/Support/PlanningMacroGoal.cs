using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Nom.Data;
using Nom.Data.Plan;

namespace Nom.Orch.Services.Support
{
    /// <summary>
    /// The macro goal shuffle steers a household's plan toward. Every member eats the same
    /// plate scaled to their own calorie target, so the plate's macro mix (grams per kcal) is
    /// what reaches each person; the best shared mix is the members' goals summed and divided
    /// by their summed calories. Each member's own goal wins field by field, falling back to
    /// the household goal (and, for calories, the same 2,000 kcal default portions use).
    /// </summary>
    public sealed class PlanningMacroGoal
    {
        public const decimal DefaultDailyCalories = 2000m;

        /// <summary>Per-person daily targets (members' mean), for the absolute per-serving scoring.</summary>
        public decimal? CaloriesTarget { get; init; }
        public decimal? ProteinGramsTarget { get; init; }
        public decimal? CarbGramsTarget { get; init; }
        public decimal? FatGramsTarget { get; init; }

        /// <summary>Target plate mix in grams per kcal; null when no member has a goal for that macro.</summary>
        public decimal? ProteinPerKcal { get; init; }
        public decimal? CarbPerKcal { get; init; }
        public decimal? FatPerKcal { get; init; }

        public bool HasAnyTarget =>
            CaloriesTarget.HasValue || ProteinGramsTarget.HasValue || CarbGramsTarget.HasValue || FatGramsTarget.HasValue;

        public static PlanningMacroGoal Combine(IReadOnlyCollection<MacroGoalEntity?> memberGoals, MacroGoalEntity? householdGoal)
        {
            var members = memberGoals.Count > 0 ? memberGoals : new MacroGoalEntity?[] { null };
            var effective = members.Select(own => new MemberTargets(
                own?.CaloriesTarget ?? householdGoal?.CaloriesTarget,
                own?.ProteinGramsTarget ?? householdGoal?.ProteinGramsTarget,
                own?.CarbGramsTarget ?? householdGoal?.CarbGramsTarget,
                own?.FatGramsTarget ?? householdGoal?.FatGramsTarget)).ToList();

            decimal? Mean(IEnumerable<decimal?> values)
            {
                var set = values.Where(v => v.HasValue).Select(v => v!.Value).ToList();
                return set.Count == 0 ? null : set.Sum() / set.Count;
            }

            decimal? PerKcal(Func<MemberTargets, decimal?> grams)
            {
                var withGoal = effective.Where(e => grams(e).HasValue).ToList();
                if (withGoal.Count == 0) return null;
                var kcal = withGoal.Sum(e => e.Calories ?? DefaultDailyCalories);
                return kcal <= 0m ? null : withGoal.Sum(e => grams(e)!.Value) / kcal;
            }

            return new PlanningMacroGoal
            {
                CaloriesTarget = Mean(effective.Select(e => e.Calories)),
                ProteinGramsTarget = Mean(effective.Select(e => e.Protein)),
                CarbGramsTarget = Mean(effective.Select(e => e.Carbs)),
                FatGramsTarget = Mean(effective.Select(e => e.Fat)),
                ProteinPerKcal = PerKcal(e => e.Protein),
                CarbPerKcal = PerKcal(e => e.Carbs),
                FatPerKcal = PerKcal(e => e.Fat),
            };
        }

        private sealed record MemberTargets(decimal? Calories, decimal? Protein, decimal? Carbs, decimal? Fat);

        public static async Task<PlanningMacroGoal> LoadAsync(ApplicationDbContext db, long householdId)
        {
            var personIds = await db.HouseholdMembers
                .AsNoTracking()
                .Where(hm => hm.HouseholdId == householdId && hm.IsActive)
                .Select(hm => hm.PersonId)
                .Distinct()
                .ToListAsync();

            var personGoals = await db.MacroGoals
                .AsNoTracking()
                .Where(g => g.PersonId != null && personIds.Contains(g.PersonId.Value))
                .ToListAsync();
            var byPerson = personGoals
                .GroupBy(g => g.PersonId!.Value)
                .ToDictionary(g => g.Key, g => g.First());

            var householdGoal = await db.MacroGoals
                .AsNoTracking()
                .FirstOrDefaultAsync(g => g.HouseholdId == householdId);

            return Combine(personIds.Select(id => byPerson.GetValueOrDefault(id)).ToList(), householdGoal);
        }
    }
}

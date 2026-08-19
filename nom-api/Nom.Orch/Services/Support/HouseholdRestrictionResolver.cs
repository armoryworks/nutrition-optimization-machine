using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Nom.Data;
using Nom.Data.Plan;

namespace Nom.Orch.Services.Support
{
    /// <summary>What a household's active dietary restrictions forbid, in ingredient terms.</summary>
    public sealed class RestrictedIngredientSet
    {
        public HashSet<long> IngredientIds { get; } = new();
        /// <summary>Any active restriction (or its category's criteria) at severity ≥ 4 — allergy/medical.</summary>
        public bool HasSevere { get; set; }
        public static readonly RestrictedIngredientSet Empty = new();
    }

    /// <summary>
    /// Turns a household's restrictions into a concrete set of ingredient ids that meal
    /// planning, search and top-up must avoid. Restrictions saved through the UI reference
    /// a *category* (RestrictionTypeId, e.g. "Nut Allergy"); the category's curated
    /// <see cref="RestrictionCriterionEntity"/> rows (exact ingredient or ILIKE name pattern,
    /// with severity) are what make it enforceable. Direct IngredientId restrictions still
    /// count too. Plan-wide restrictions (PersonId NULL) apply through the members' plans.
    /// </summary>
    public sealed class HouseholdRestrictionResolver
    {
        private readonly ApplicationDbContext _db;

        public HouseholdRestrictionResolver(ApplicationDbContext db) => _db = db;

        public async Task<RestrictedIngredientSet> ResolveAsync(long householdId)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);

            var memberIds = await _db.HouseholdMembers
                .Where(hm => hm.HouseholdId == householdId && hm.IsActive)
                .Select(hm => hm.PersonId)
                .Distinct()
                .ToListAsync();
            if (memberIds.Count == 0) return RestrictedIngredientSet.Empty;

            var memberPlanIds = await _db.PlanParticipants
                .Where(pp => memberIds.Contains(pp.PersonId))
                .Select(pp => pp.PlanId)
                .Distinct()
                .ToListAsync();

            var restrictions = await _db.Restrictions
                .Where(r => (r.EndDate == null || r.EndDate >= today)
                         && (r.BeginDate == null || r.BeginDate <= today)
                         && ((r.PersonId != null && memberIds.Contains(r.PersonId.Value))
                             || (r.PersonId == null && r.PlanId != null && memberPlanIds.Contains(r.PlanId.Value))))
                .Select(r => new { r.IngredientId, r.RestrictionTypeId, r.Severity })
                .ToListAsync();
            if (restrictions.Count == 0) return RestrictedIngredientSet.Empty;

            var result = new RestrictedIngredientSet();
            foreach (var r in restrictions)
            {
                if (r.IngredientId is { } id) result.IngredientIds.Add(id);
                if ((r.Severity ?? 0) >= 4) result.HasSevere = true;
            }

            var typeIds = restrictions.Where(r => r.RestrictionTypeId != null).Select(r => r.RestrictionTypeId!.Value).Distinct().ToList();
            if (typeIds.Count == 0) return result;

            var criteria = await _db.Set<RestrictionCriterionEntity>()
                .Where(c => typeIds.Contains(c.RestrictionTypeId))
                .Select(c => new { c.IngredientId, c.IngredientPattern, c.Severity })
                .ToListAsync();

            foreach (var c in criteria)
            {
                if (c.IngredientId is { } id) result.IngredientIds.Add(id);
                if (c.Severity >= 4) result.HasSevere = true;
            }

            var patterns = criteria
                .Where(c => !string.IsNullOrWhiteSpace(c.IngredientPattern))
                .Select(c => c.IngredientPattern!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (patterns.Count == 0) return result;

            foreach (var id in await MatchIngredientsAsync(patterns))
                result.IngredientIds.Add(id);

            return result;
        }

        /// <summary>Ingredient ids whose name or any alias matches one of the SQL LIKE patterns (case-insensitive).</summary>
        private async Task<List<long>> MatchIngredientsAsync(List<string> patterns)
        {
            if (_db.Database.IsRelational())
            {
                var ids = new List<long>();
                foreach (var pattern in patterns)
                {
                    ids.AddRange(await _db.Ingredients
                        .Where(i => EF.Functions.ILike(i.Name, pattern)
                                 || i.Aliases.Any(a => EF.Functions.ILike(a.AliasName, pattern)))
                        .Select(i => i.Id)
                        .ToListAsync());
                }
                return ids;
            }

            // In-memory provider (tests): emulate ILIKE client-side.
            var regexes = patterns.Select(p => new Regex("^" + Regex.Escape(p).Replace("%", ".*").Replace("_", ".") + "$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)).ToList();
            var all = await _db.Ingredients
                .Select(i => new { i.Id, i.Name, Aliases = i.Aliases.Select(a => a.AliasName).ToList() })
                .ToListAsync();
            return all
                .Where(i => regexes.Any(rx => rx.IsMatch(i.Name) || i.Aliases.Any(a => rx.IsMatch(a))))
                .Select(i => i.Id)
                .ToList();
        }
    }
}

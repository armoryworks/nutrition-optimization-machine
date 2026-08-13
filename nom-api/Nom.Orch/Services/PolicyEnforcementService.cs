using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;

using Nom.Data;
using Nom.Orch.Interfaces;

namespace Nom.Orch.Services
{
    /// <summary>Feature-gate key constants (NOM-owned enum; unknown keys in stored policies are ignored).</summary>
    public static class FeatureGateKeys
    {
        public const string Shuffle = "shuffle";
        public const string RecipeImport = "recipe_import";
        public const string RecipeCreate = "recipe_create";
        public const string RecipeEdit = "recipe_edit";
    }

    public class PolicyEnforcementService : IPolicyEnforcementService
    {
        private readonly ApplicationDbContext _context;

        public PolicyEnforcementService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<bool> IsFeatureGatedAsync(long personId, long householdId, string gateKey)
        {
            var gatesJson = await _context.MemberPolicies
                .Where(p => p.PersonId == personId && p.HouseholdId == householdId)
                .Select(p => p.FeatureGates)
                .FirstOrDefaultAsync();
            if (string.IsNullOrWhiteSpace(gatesJson))
            {
                return false;
            }

            try
            {
                using var doc = JsonDocument.Parse(gatesJson);
                return doc.RootElement.ValueKind == JsonValueKind.Object
                    && doc.RootElement.TryGetProperty(gateKey, out var v)
                    && v.ValueKind == JsonValueKind.False;
            }
            catch (JsonException)
            {
                // Malformed policy payloads never lock users out of features.
                return false;
            }
        }

        public async Task<bool> IsFeatureGatedAnywhereAsync(long personId, string gateKey)
        {
            var gateJsons = await _context.MemberPolicies
                .Where(p => p.PersonId == personId
                    && _context.HouseholdMembers.Any(hm =>
                        hm.HouseholdId == p.HouseholdId && hm.PersonId == personId && hm.IsActive))
                .Select(p => p.FeatureGates)
                .ToListAsync();

            foreach (var json in gateJsons)
            {
                if (string.IsNullOrWhiteSpace(json)) continue;
                try
                {
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.ValueKind == JsonValueKind.Object
                        && doc.RootElement.TryGetProperty(gateKey, out var v)
                        && v.ValueKind == JsonValueKind.False)
                    {
                        return true;
                    }
                }
                catch (JsonException) { /* malformed policies never lock features */ }
            }
            return false;
        }

        public Task<bool> IsCuratedOnlyAsync(long personId, long householdId) =>
            _context.MemberPolicies
                .Where(p => p.PersonId == personId && p.HouseholdId == householdId)
                .Select(p => p.CuratedOnly)
                .FirstOrDefaultAsync();

        public Task<List<long>> GetHouseholdsPlanningRecipeAsync(long recipeId)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            return _context.MealPlans
                .Where(mp => mp.RecipeId == recipeId && mp.Date >= today)
                .Select(mp => mp.HouseholdId)
                .Distinct()
                .ToListAsync();
        }

        public Task<List<long>> GetLockedIngredientIdsAsync(long householdId)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            return _context.HouseholdMembers
                .Where(hm => hm.HouseholdId == householdId && hm.IsActive)
                .SelectMany(hm => hm.Person.Restrictions)
                .Where(r => r.Locked && r.IngredientId.HasValue
                    && (r.EndDate == null || r.EndDate >= today)
                    && (r.BeginDate == null || r.BeginDate <= today))
                .Select(r => r.IngredientId!.Value)
                .Distinct()
                .ToListAsync();
        }
    }
}

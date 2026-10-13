using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Nom.Data;
using Nom.Data.Plan;
using Nom.Data.Reference;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Household;
using Nom.Orch.Services.Support;

namespace Nom.Orch.Services
{
    public class KitchenToolService : IKitchenToolService
    {
        public const string ModePreferenceKey = "kitchen_tools_mode";

        private readonly ApplicationDbContext _context;

        public KitchenToolService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IReadOnlyDictionary<long, KitchenToolDefinition>> GetToolSetAsync()
        {
            var approved = await _context.Set<ReferenceEntity>()
                .AsNoTracking()
                .Where(r => !r.IsDeleted && r.Groups!.Any(g => g.Id == (long)ReferenceDiscriminatorEnum.KitchenToolType))
                .Select(r => new { r.Id, r.Name, r.Description })
                .ToListAsync();

            var set = new Dictionary<long, KitchenToolDefinition>(KitchenToolCatalog.ById);
            foreach (var r in approved.Where(r => !set.ContainsKey(r.Id)))
            {
                set[r.Id] = KitchenToolCatalog.Approved(r.Id, r.Name, r.Description);
            }
            return set;
        }

        public async Task<KitchenSettingsModel> GetSettingsAsync(long householdId)
        {
            var tools = await GetToolSetAsync();
            var answers = await LoadAnswersAsync(householdId);
            return new KitchenSettingsModel
            {
                Mode = await GetModeAsync(householdId),
                Tools = tools.Values.Select(t => new KitchenToolModel
                {
                    Id = t.Id,
                    Key = t.Key,
                    Name = t.Name,
                    Category = t.Category,
                    OwnedByDefault = t.OwnedByDefault,
                    Answered = answers.ContainsKey(t.Id),
                    Owned = answers.TryGetValue(t.Id, out var owned) ? owned : t.OwnedByDefault,
                }).ToList(),
            };
        }

        public async Task<KitchenSettingsModel> UpdateSettingsAsync(long householdId, KitchenSettingsUpdateModel update)
        {
            if (update.Mode != null)
            {
                if (!KitchenToolModes.IsValid(update.Mode))
                    throw new ArgumentException($"Unknown kitchen tools mode '{update.Mode}'.");

                var pref = await _context.HouseholdPreferences
                    .FirstOrDefaultAsync(p => p.HouseholdId == householdId && p.PreferenceKey == ModePreferenceKey);
                if (pref == null)
                {
                    pref = new HouseholdPreferenceEntity
                    {
                        HouseholdId = householdId,
                        PreferenceKey = ModePreferenceKey,
                        DataType = "string",
                    };
                    _context.HouseholdPreferences.Add(pref);
                }
                pref.PreferenceValue = update.Mode;
            }

            if (update.Tools is { Count: > 0 })
            {
                var tools = await GetToolSetAsync();
                var unknown = update.Tools.Where(a => !tools.ContainsKey(a.ToolId)).Select(a => a.ToolId).ToList();
                if (unknown.Count > 0)
                    throw new ArgumentException($"Unknown kitchen tool id(s): {string.Join(", ", unknown)}.");

                var ids = update.Tools.Select(a => a.ToolId).ToList();
                var rows = await _context.HouseholdTools
                    .Where(t => t.HouseholdId == householdId && ids.Contains(t.ToolId))
                    .ToListAsync();

                foreach (var answer in update.Tools)
                {
                    var existing = rows.Where(r => r.ToolId == answer.ToolId).ToList();
                    if (answer.Owned == null)
                    {
                        _context.HouseholdTools.RemoveRange(existing);
                        continue;
                    }

                    var row = existing.FirstOrDefault();
                    if (row == null)
                    {
                        _context.HouseholdTools.Add(new HouseholdToolEntity
                        {
                            HouseholdId = householdId,
                            ToolId = answer.ToolId,
                            IsAvailable = answer.Owned.Value,
                        });
                    }
                    else
                    {
                        row.IsAvailable = answer.Owned.Value;
                        _context.HouseholdTools.RemoveRange(existing.Skip(1));
                    }
                }
            }

            await _context.SaveChangesAsync();
            return await GetSettingsAsync(householdId);
        }

        public async Task<string> GetModeAsync(long householdId)
        {
            var value = await _context.HouseholdPreferences
                .AsNoTracking()
                .Where(p => p.HouseholdId == householdId && p.PreferenceKey == ModePreferenceKey)
                .Select(p => p.PreferenceValue)
                .FirstOrDefaultAsync();
            return KitchenToolModes.IsValid(value) ? value! : KitchenToolModes.Default;
        }

        public async Task<IReadOnlySet<long>> GetOwnedToolIdsAsync(long householdId, IReadOnlyDictionary<long, KitchenToolDefinition>? tools = null)
        {
            return KitchenToolEvaluator.ResolveOwned(await LoadAnswersAsync(householdId), tools ?? await GetToolSetAsync());
        }

        public async Task<RecipeToolCheckModel?> CheckRecipeAsync(long householdId, long recipeId)
        {
            var recipe = await _context.Recipes
                .AsNoTracking()
                .Where(r => r.Id == recipeId)
                .Select(r => new
                {
                    r.Id,
                    r.Name,
                    Linked = r.RecipeTools!.Select(t => new LinkedTool(t.ToolId, t.Source)).ToList(),
                    Steps = r.RecipeSteps!.Select(s => s.Summary + " " + s.Description).ToList(),
                })
                .FirstOrDefaultAsync();
            if (recipe == null) return null;

            var tools = await GetToolSetAsync();
            var owned = await GetOwnedToolIdsAsync(householdId, tools);
            var listed = recipe.Linked.Where(l => tools.ContainsKey(l.ToolId)).ToList();
            var required = KitchenToolEvaluator.RequiredTools(listed, recipe.Name, recipe.Steps, tools);
            var check = KitchenToolEvaluator.Evaluate(required, owned, tools);

            return new RecipeToolCheckModel
            {
                RecipeId = recipe.Id,
                Fit = FitName(check.Fit),
                Mode = await GetModeAsync(householdId),
                Inferred = !listed.Any(l => l.Source == null),
                Needs = check.Needs.Select(n => new RecipeToolNeedModel
                {
                    ToolId = n.ToolId,
                    ToolName = n.ToolName,
                    Fit = FitName(n.Fit),
                    UsingToolName = n.UsingToolId is long u && u != n.ToolId && tools.TryGetValue(u, out var t) ? t.Name : null,
                    Note = n.Note,
                }).ToList(),
            };
        }

        public static string FitName(KitchenToolFit fit) => fit switch
        {
            KitchenToolFit.Ready => "ready",
            KitchenToolFit.Substitute => "substitute",
            KitchenToolFit.Harder => "harder",
            _ => "missing",
        };

        private async Task<IReadOnlyDictionary<long, bool>> LoadAnswersAsync(long householdId)
        {
            var rows = await _context.HouseholdTools
                .AsNoTracking()
                .Where(t => t.HouseholdId == householdId)
                .Select(t => new { t.ToolId, t.IsAvailable })
                .ToListAsync();
            return rows.GroupBy(r => r.ToolId).ToDictionary(g => g.Key, g => g.Any(r => r.IsAvailable));
        }
    }
}

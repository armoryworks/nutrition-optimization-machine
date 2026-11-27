using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Nom.Data;
using Nom.Data.Plan;
using Nom.Data.Recipe;

namespace Nom.Orch.Services.Support
{
    /// <summary>
    /// Why the recipe auto-approval lane leaves a recipe waiting although its policy and vetting pass (plan item ck21:
    /// approval follows understanding). The public pool is what households see, so a recipe enters it only once the stored
    /// diet gate understands it: the active build has decided it and builds no severe set (a set being built has decided
    /// nothing, and requests of its type hold every recipe until it is complete), no severe set holds it as unknown by its rules (no line,
    /// a food or word not understood, a title, method or referral hold), and both models of the second reader's pair have
    /// read its current text under the current prompt (<see cref="DietSecondGate"/>). A severe set's hold by the second
    /// reader itself does not keep a recipe waiting: the recipe is understood and stays held for that type only. Nothing
    /// here takes back an approval. Off a database where the stored gate is not in use (<see cref="DietGateStore.InUseAsync"/>)
    /// nothing waits.
    /// </summary>
    public static class DietApprovalHolds
    {
        public const string NotDecidedKey = "not yet decided by the diet check";
        public const string UnknownKey = "held as unknown by a severe diet type";
        public const string UnreadKey = "not yet read by the second reader";

        /// <summary>The waiting reason of each recipe that waits; a recipe not in the result may be approved.</summary>
        public static async Task<Dictionary<long, string>> ReasonsAsync(ApplicationDbContext db, IReadOnlyList<RecipeEntity> recipes, AutoApprovalOptions options, CancellationToken cancellationToken = default)
        {
            var result = new Dictionary<long, string>();
            if (recipes.Count == 0 || (!options.RequireDietUnderstood && !options.RequireSecondReading) || !await DietGateStore.InUseAsync(db)) return result;
            var ids = recipes.Select(r => r.Id).ToList();
            if (options.RequireDietUnderstood)
            {
                if (await DietGateStore.ActiveBuildAsync(db) is not { } build)
                {
                    foreach (var id in ids) result[id] = NotDecidedKey;
                    return result;
                }
                if (await db.DietGateSets.AsNoTracking().AnyAsync(s => s.BuildId == build && s.Severe && s.State == DietGateStore.Building, cancellationToken))
                {
                    foreach (var id in ids) result[id] = NotDecidedKey;
                    return result;
                }
                var decided = (await db.DietRecipeStates.AsNoTracking().Standing(db)
                        .Where(s => s.BuildId == build && ids.Contains(s.RecipeId))
                        .Select(s => s.RecipeId)
                        .ToListAsync(cancellationToken))
                    .ToHashSet();
                var severe = db.DietGateSets.AsNoTracking().Where(s => s.BuildId == build && s.Severe && s.State == DietGateStore.Complete).Select(s => s.Id);
                var unknown = (await db.DietRecipeFlags.AsNoTracking()
                        .Where(f => ids.Contains(f.RecipeId) && severe.Contains(f.SetId) && f.Verdict == DietVerdict.Unknown
                            && f.Reason != DietFlagReason.SecondUnread && f.Reason != DietFlagReason.SecondReader)
                        .Select(f => f.RecipeId)
                        .Distinct()
                        .ToListAsync(cancellationToken))
                    .ToHashSet();
                foreach (var id in ids)
                {
                    if (!decided.Contains(id)) result[id] = NotDecidedKey;
                    else if (unknown.Contains(id)) result[id] = UnknownKey;
                }
            }
            if (options.RequireSecondReading)
            {
                var readings = await DietSecondGate.LoadAsync(db, ids, cancellationToken);
                foreach (var recipe in recipes.Where(r => !result.ContainsKey(r.Id)))
                    if (DietSecondGate.Summary(readings.GetValueOrDefault(recipe.Id), TextSignature(recipe)).Length == 0) result[recipe.Id] = UnreadKey;
            }
            return result;
        }

        /// <summary>The signature of a loaded recipe's text as the second reader is shown it (<see cref="DietSecondGate.TextSignature"/>).</summary>
        public static string TextSignature(RecipeEntity recipe) =>
            DietSecondGate.TextSignature(recipe.Name,
                (recipe.RecipeIngredients ?? new List<RecipeIngredientEntity>()).Where(ri => !ri.IsDeleted).OrderBy(ri => ri.SortOrder).ThenBy(ri => ri.IngredientId)
                    .Select(ri => DietSecondGate.LineText(ri.RawLine, ri.Ingredient?.Name)),
                (recipe.RecipeSteps ?? new List<RecipeStepEntity>()).Where(s => !s.IsDeleted).OrderBy(s => s.StepNumber).Select(s => s.Description));
    }
}

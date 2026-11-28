using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Nom.Data;
using Nom.Data.Curation;
using Nom.Data.Recipe;

namespace Nom.Orch.Services.Support
{
    /// <summary>
    /// What a write of verified wording rows can change in the diet reading, held in the writer's own transaction
    /// (staging checkpoint 9, U1 and U11). A food row proves an applied model pick of its food and wording
    /// (<see cref="DietGateStoreBuilder.UnprovenAsync"/>), so writing, changing or taking out such a row changes the reading
    /// of exactly the lines on that food whose text names the pick's wording (<see cref="DietGateStoreBuilder.OnUnprovenLink"/>).
    /// The statement trigger of <c>db/custom-objects.sql</c> cannot read a line's wording as the reading does, so it holds
    /// every recipe on the food; a writer that holds the precise set sets <see cref="WriterSetting"/> for its transaction and
    /// the trigger leaves its food part to the writer. A category row is still held by the trigger, by its name.
    /// </summary>
    public static class DietGateWordingHolds
    {
        /// <summary>The transaction-local setting by which a writer tells the wording trigger it held its food rows' recipes itself.</summary>
        public const string WriterSetting = "nom.diet_wording_holds";

        /// <summary>A written row's verdict and food before or after the write; only a live food row is read by the reading.</summary>
        public sealed record Written(string Wording, string? Verdict, long? IngredientId, bool IsDeleted);

        /// <summary>The recipes with a line on a food of a written live food row whose text names the wording of an applied model pick of that food and wording.</summary>
        public static async Task<List<long>> RecipesAsync(ApplicationDbContext db, IEnumerable<Written> written, CancellationToken cancellationToken = default)
        {
            var pairs = written
                .Where(w => !w.IsDeleted && w.Verdict == IngredientWordingVerdict.Food && w.IngredientId != null)
                .Select(w => (Food: w.IngredientId!.Value, Wording: IngredientNameNormalizer.WordingKey(w.Wording)))
                .Where(p => p.Wording.Length > 0)
                .ToHashSet();
            if (pairs.Count == 0) return new List<long>();

            var changed = new HashSet<(long Food, string Wording)>();
            foreach (var chunk in pairs.Select(p => p.Food.ToString(CultureInfo.InvariantCulture)).Distinct().Chunk(1000))
            {
                var picks = await db.FoodCatalogProposals.AsNoTracking()
                    .Where(p => p.Field == FoodCatalogReviewService.FdcLinkField && p.Status == FoodProposalStatus.Applied && p.Source.StartsWith("ai:")
                        && p.CurrentValue != null && p.ProposedValue != null && chunk.Contains(p.ProposedValue))
                    .Select(p => new { p.CurrentValue, p.ProposedValue })
                    .ToListAsync(cancellationToken);
                foreach (var pick in picks)
                {
                    if (!long.TryParse(pick.ProposedValue, NumberStyles.None, CultureInfo.InvariantCulture, out var food)) continue;
                    var pair = (food, IngredientNameNormalizer.WordingKey(pick.CurrentValue!));
                    if (pairs.Contains(pair)) changed.Add(pair);
                }
            }
            if (changed.Count == 0) return new List<long>();

            var links = DietGateStoreBuilder.UnprovenOf(changed);
            var recipes = new HashSet<long>();
            foreach (var chunk in links.Keys.Chunk(200))
            {
                var lines = await db.RecipeIngredients.AsNoTracking().IgnoreQueryFilters()
                    .Where(ri => chunk.Contains(ri.IngredientId))
                    .Select(ri => new { ri.RecipeId, ri.IngredientId, ri.RawLine })
                    .ToListAsync(cancellationToken);
                foreach (var line in lines)
                    if (DietGateStoreBuilder.OnUnprovenLink(line.RawLine, links[line.IngredientId])) recipes.Add(line.RecipeId);
            }
            return recipes.Order().ToList();
        }

        /// <summary>
        /// On a relational database inside the writer's transaction: holds <see cref="RecipesAsync"/> in the write log and sets
        /// <see cref="WriterSetting"/>, so the trigger does not hold every recipe on the rows' foods again, and answers the recipes
        /// held; elsewhere nothing.
        /// </summary>
        public static async Task<List<long>> HoldAsync(ApplicationDbContext db, IEnumerable<Written> written, CancellationToken cancellationToken = default)
        {
            if (!db.Database.IsRelational() || db.Database.CurrentTransaction == null) return new List<long>();
            var recipes = await RecipesAsync(db, written, cancellationToken);
            await db.Database.ExecuteSqlRawAsync("SELECT set_config({0}, 'writer', true)", new object[] { WriterSetting }, cancellationToken);
            foreach (var chunk in recipes.Chunk(50000))
                await db.Database.ExecuteSqlRawAsync("SELECT plan.\"DietGateHoldRecipes\"({0})", new object[] { chunk }, cancellationToken);
            return recipes;
        }
    }
}

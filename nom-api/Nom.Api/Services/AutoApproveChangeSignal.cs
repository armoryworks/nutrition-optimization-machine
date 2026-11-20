using Microsoft.EntityFrameworkCore;
using Nom.Data;
using Nom.Data.Recipe;

namespace Nom.Api.Services
{
    /// <summary>
    /// A cheap fingerprint of everything the auto-approval sweep decides on: the latest change and
    /// the count of recipes, recipe lines and ingredients, and how many ingredients are curated.
    /// A link applied, a recipe re-vetted or a line reclassified changes it; when it is unchanged a
    /// new sweep would examine the same recipes and reach the same verdicts.
    /// </summary>
    public static class AutoApproveChangeSignal
    {
        public static async Task<string> FingerprintAsync(ApplicationDbContext db, CancellationToken cancellationToken)
        {
            var recipes = await db.Recipes.AsNoTracking().IgnoreQueryFilters()
                .GroupBy(_ => 1)
                .Select(g => new { Count = g.Count(), Last = g.Max(r => r.LastModifiedDate ?? r.CreatedDate) })
                .FirstOrDefaultAsync(cancellationToken);
            var lines = await db.RecipeIngredients.AsNoTracking().IgnoreQueryFilters()
                .GroupBy(_ => 1)
                .Select(g => new { Count = g.Count(), Last = g.Max(ri => ri.LastModifiedDate ?? ri.CreatedDate) })
                .FirstOrDefaultAsync(cancellationToken);
            var ingredients = await db.Ingredients.AsNoTracking().IgnoreQueryFilters()
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    Count = g.Count(),
                    Curated = g.Count(i => i.CurationStatusId == (long)CurationStatusEnum.Curated),
                    Last = g.Max(i => i.LastModifiedDate ?? i.CreatedDate),
                })
                .FirstOrDefaultAsync(cancellationToken);

            return string.Join("|",
                recipes?.Count, recipes?.Last.Ticks,
                lines?.Count, lines?.Last.Ticks,
                ingredients?.Count, ingredients?.Curated, ingredients?.Last.Ticks);
        }
    }
}

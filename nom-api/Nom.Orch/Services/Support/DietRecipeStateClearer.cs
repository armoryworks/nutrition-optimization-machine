using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Nom.Data;
using Nom.Data.Recipe;

namespace Nom.Orch.Services.Support
{
    /// <summary>
    /// Makes a recipe undecided in the diet gate's store in the same save that writes one of its lines or its servings
    /// (review CK10S-3): its <see cref="Nom.Data.Plan.DietRecipeStateEntity"/> rows are removed, so every stored set holds
    /// it (<see cref="DietGateStore.WithoutHeld(IQueryable{RecipeEntity}, ApplicationDbContext, RestrictedIngredientSet)"/>)
    /// until the build job has decided it again, instead of reading the decision made on its old lines.
    /// </summary>
    public sealed class DietRecipeStateClearer : SaveChangesInterceptor
    {
        /// <summary>The one clearer the API's contexts share.</summary>
        public static DietRecipeStateClearer Shared { get; } = new();

        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            if (eventData.Context is ApplicationDbContext db)
                foreach (var batch in Touched(db).Chunk(5000))
                    db.DietRecipeStates.RemoveRange(db.DietRecipeStates.Where(s => batch.Contains(s.RecipeId)).ToList());
            return result;
        }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context is ApplicationDbContext db) await ClearAsync(db, cancellationToken);
            return result;
        }

        /// <summary>The recipes whose lines or servings the context is about to write.</summary>
        internal static HashSet<long> Touched(ApplicationDbContext db)
        {
            var ids = new HashSet<long>();
            foreach (var entry in db.ChangeTracker.Entries())
            {
                if (entry.Entity is RecipeIngredientEntity line && entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
                {
                    if (line.RecipeId > 0) ids.Add(line.RecipeId);
                    if (entry.State == EntityState.Modified && entry.Property(nameof(RecipeIngredientEntity.RecipeId)).OriginalValue is long before && before > 0) ids.Add(before);
                }
                else if (entry.Entity is RecipeEntity recipe && entry.State == EntityState.Modified && recipe.Id > 0
                    && (entry.Property(nameof(RecipeEntity.Servings)).IsModified || entry.Property(nameof(RecipeEntity.RecipeServings)).IsModified || entry.Property(nameof(RecipeEntity.IsDeleted)).IsModified))
                    ids.Add(recipe.Id);
            }
            return ids;
        }

        private static async Task ClearAsync(ApplicationDbContext db, CancellationToken cancellationToken)
        {
            foreach (var batch in Touched(db).Chunk(5000))
                db.DietRecipeStates.RemoveRange(await db.DietRecipeStates.Where(s => batch.Contains(s.RecipeId)).ToListAsync(cancellationToken));
        }
    }
}

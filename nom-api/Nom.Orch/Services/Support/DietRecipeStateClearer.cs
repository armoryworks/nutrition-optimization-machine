using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Nom.Data;
using Nom.Data.Plan;
using Nom.Data.Recipe;

namespace Nom.Orch.Services.Support
{
    /// <summary>
    /// Makes a recipe undecided in the diet gate's store in the same save that writes one of its lines or its servings
    /// (review CK10S-3), so every stored set holds it
    /// (<see cref="DietGateStore.WithoutHeld(IQueryable{RecipeEntity}, ApplicationDbContext, RestrictedIngredientSet)"/>)
    /// until the build job has decided it again, instead of reading the decision made on its old lines. On a relational
    /// database the save logs the recipe in the write log (<see cref="DietGateWriteEntity"/>), which holds it from the
    /// commit (<see cref="DietGateStore.Standing"/>) as the triggers of <c>db/custom-objects.sql</c> do: removing the
    /// tracked <see cref="DietRecipeStateEntity"/> rows expected each row to be there at the save, while the build job
    /// replaces a recipe's state in its own transaction, so a long writer such as the alcohol retention apply failed with a
    /// concurrency error when the job replaced a state between the read and the save (staging checkpoint 8, T15).
    /// Elsewhere the state rows are removed.
    /// </summary>
    public sealed class DietRecipeStateClearer : SaveChangesInterceptor
    {
        /// <summary>The one clearer the API's contexts share.</summary>
        public static DietRecipeStateClearer Shared { get; } = new();

        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            if (eventData.Context is ApplicationDbContext db)
            {
                if (db.Database.IsRelational()) Hold(db);
                else
                    foreach (var batch in Touched(db).Chunk(5000))
                        db.DietRecipeStates.RemoveRange(db.DietRecipeStates.Where(s => batch.Contains(s.RecipeId)).ToList());
            }
            return result;
        }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context is ApplicationDbContext db) await ClearAsync(db, cancellationToken);
            return result;
        }

        /// <summary>The recipes whose lines, steps, servings, title, source or book the context is about to write.</summary>
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
                else if (entry.Entity is RecipeStepEntity step && entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
                {
                    if (step.RecipeId > 0) ids.Add(step.RecipeId);
                    if (entry.State == EntityState.Modified && entry.Property(nameof(RecipeStepEntity.RecipeId)).OriginalValue is long before && before > 0) ids.Add(before);
                }
                else if (entry.Entity is RecipeEntity recipe && entry.State == EntityState.Modified && recipe.Id > 0
                    && (entry.Property(nameof(RecipeEntity.Servings)).IsModified || entry.Property(nameof(RecipeEntity.RecipeServings)).IsModified || entry.Property(nameof(RecipeEntity.IsDeleted)).IsModified
                        || entry.Property(nameof(RecipeEntity.Name)).IsModified || entry.Property(nameof(RecipeEntity.SourceUrl)).IsModified || entry.Property(nameof(RecipeEntity.ScrapedAtUtc)).IsModified
                        || entry.Property(nameof(RecipeEntity.SourceAttribution)).IsModified))
                    ids.Add(recipe.Id);
            }
            return ids;
        }

        private static void Hold(ApplicationDbContext db)
        {
            foreach (var id in Touched(db).Order())
                db.DietGateWrites.Add(new DietGateWriteEntity { RecipeId = id });
        }

        private static async Task ClearAsync(ApplicationDbContext db, CancellationToken cancellationToken)
        {
            if (db.Database.IsRelational())
            {
                Hold(db);
                return;
            }
            foreach (var batch in Touched(db).Chunk(5000))
                db.DietRecipeStates.RemoveRange(await db.DietRecipeStates.Where(s => batch.Contains(s.RecipeId)).ToListAsync(cancellationToken));
        }
    }
}

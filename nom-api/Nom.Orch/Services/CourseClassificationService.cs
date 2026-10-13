using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nom.Data;
using Nom.Data.Recipe;
using Nom.Data.Reference;
using Nom.Orch.Interfaces;
using Nom.Orch.Services.Support;

namespace Nom.Orch.Services
{
    public class CourseClassificationService : ICourseClassificationService
    {
        public const long SnackType = 3104, DessertType = 3105;
        public const long SnacksCategory = 1103, DessertsCategory = 1104;
        private const int Batch = 200;

        private readonly ApplicationDbContext _context;
        private readonly ILogger<CourseClassificationService> _logger;

        public CourseClassificationService(ApplicationDbContext context, ILogger<CourseClassificationService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<CourseClassificationResult> ClassifyAsync(CancellationToken cancellationToken = default)
        {
            var nutrients = await _context.Nutrients
                .AsNoTracking()
                .Where(n => n.Name == "Calories" || n.Name == "Total Sugars" || n.Name == "Added Sugars")
                .ToDictionaryAsync(n => n.Name, n => n.Id, cancellationToken);
            if (!nutrients.TryGetValue("Calories", out var kcalId)) return new CourseClassificationResult(0, 0, 0, 0);
            var sugarsId = nutrients.GetValueOrDefault("Total Sugars", -1);
            var addedId = nutrients.GetValueOrDefault("Added Sugars", -1);

            var ids = await _context.Recipes
                .Where(r => !r.IsDeleted
                    && (r.RecipeTypes!.Any(t => t.Id == SnackType || t.Id == DessertType)
                        || r.RecipeCategories!.Any(c => c.CategoryId == SnacksCategory || c.CategoryId == DessertsCategory)))
                .OrderBy(r => r.Id)
                .Select(r => r.Id)
                .ToListAsync(cancellationToken);

            var snackType = await _context.Set<ReferenceEntity>().FirstOrDefaultAsync(r => r.Id == SnackType, cancellationToken);
            var dessertType = await _context.Set<ReferenceEntity>().FirstOrDefaultAsync(r => r.Id == DessertType, cancellationToken);
            var categoriesExist = await _context.Set<ReferenceEntity>().CountAsync(r => r.Id == SnacksCategory || r.Id == DessertsCategory, cancellationToken) == 2;
            if (snackType == null || dessertType == null || !categoriesExist)
            {
                _logger.LogWarning("Course classification skipped: snack/dessert type or category reference rows are missing");
                return new CourseClassificationResult(ids.Count, 0, 0, ids.Count);
            }

            int classified = 0, changed = 0, undetermined = 0;
            foreach (var chunk in ids.Chunk(Batch))
            {
                var recipes = await _context.Recipes
                    .Where(r => chunk.Contains(r.Id))
                    .Include(r => r.RecipeTypes)
                    .Include(r => r.RecipeCategories)
                    .Include(r => r.RecipeIngredients!).ThenInclude(ri => ri.Measurement)
                    .Include(r => r.RecipeIngredients!).ThenInclude(ri => ri.Ingredient).ThenInclude(i => i.IngredientNutrients)
                    .ToListAsync(cancellationToken);

                foreach (var recipe in recipes)
                {
                    var lines = (recipe.RecipeIngredients ?? Enumerable.Empty<RecipeIngredientEntity>())
                        .Select(ri =>
                        {
                            var facts = ri.Ingredient?.IngredientNutrients;
                            decimal? Fact(long id) => facts?.FirstOrDefault(n => n.NutrientId == id)?.Amount;
                            return new CourseLine(ri.Ingredient?.Name ?? string.Empty, RecipeNutritionService.GramsFor(ri), Fact(kcalId), Fact(sugarsId), Fact(addedId));
                        })
                        .ToList();

                    var verdict = CourseClassifier.Classify(lines);
                    if (verdict == null)
                    {
                        undetermined++;
                        continue;
                    }

                    classified++;
                    if (Apply(recipe, verdict.Course, snackType, dessertType)) changed++;
                }

                await _context.SaveChangesAsync(cancellationToken);
                _context.ChangeTracker.Clear();
            }

            _logger.LogInformation("Course classification: {Candidates} candidates, {Classified} classified, {Changed} changed, {Undetermined} without enough data",
                ids.Count, classified, changed, undetermined);
            return new CourseClassificationResult(ids.Count, classified, changed, undetermined);
        }

        private bool Apply(RecipeEntity recipe, SweetCourse course, ReferenceEntity snackType, ReferenceEntity dessertType)
        {
            var changed = false;
            var types = recipe.RecipeTypes ??= new List<ReferenceEntity>();
            var categories = recipe.RecipeCategories ??= new List<RecipeCategoryEntity>();

            void Want(ReferenceEntity type, long categoryId, bool on)
            {
                var hasType = types.Any(t => t.Id == type.Id);
                if (on && !hasType) { types.Add(type); changed = true; }
                if (!on && hasType) { types.Remove(types.First(t => t.Id == type.Id)); changed = true; }

                var rows = categories.Where(c => c.CategoryId == categoryId && !c.IsDeleted).ToList();
                if (on && rows.Count == 0)
                {
                    categories.Add(new RecipeCategoryEntity { RecipeId = recipe.Id, CategoryId = categoryId, CreatedDate = DateTime.UtcNow });
                    changed = true;
                }
                if (!on && rows.Count > 0)
                {
                    foreach (var row in rows) _context.Remove(row);
                    changed = true;
                }
            }

            Want(snackType, SnacksCategory, course.HasFlag(SweetCourse.Snack));
            Want(dessertType, DessertsCategory, course.HasFlag(SweetCourse.Dessert));
            return changed;
        }
    }
}

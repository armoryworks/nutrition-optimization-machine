using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nom.Data;
using Nom.Data.Plan;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Cookbook;
using Nom.Orch.Models.Recipe;

namespace Nom.Orch.Services
{
    public class CookbookOrchestrationService : ICookbookOrchestrationService
    {
        private readonly ApplicationDbContext _db;
        private readonly ILogger<CookbookOrchestrationService> _logger;

        public CookbookOrchestrationService(ApplicationDbContext db, ILogger<CookbookOrchestrationService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<List<CookbookResponseModel>> GetCookbooksAsync(long householdId)
        {
            return await _db.HouseholdCookbooks
                .Where(c => c.HouseholdId == householdId)
                .Include(c => c.Recipes)
                .AsNoTracking()
                .Select(c => MapCookbook(c))
                .ToListAsync();
        }

        public async Task<CookbookResponseModel?> GetCookbookAsync(long id)
        {
            var cookbook = await _db.HouseholdCookbooks
                .Include(c => c.Recipes)
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == id);

            return cookbook == null ? null : MapCookbook(cookbook);
        }

        public async Task<long> CreateCookbookAsync(CookbookCreateModel model)
        {
            var entity = new HouseholdCookbookEntity
            {
                HouseholdId = model.HouseholdId,
                Name = model.Name,
                Description = model.Description,
                Slug = GenerateSlug(model.Name),
                IsPublic = model.IsPublic
            };

            _db.HouseholdCookbooks.Add(entity);
            await _db.SaveChangesAsync();

            _logger.LogInformation("Created cookbook {CookbookId} for household {HouseholdId}", entity.Id, model.HouseholdId);
            return entity.Id;
        }

        public async Task<CookbookResponseModel?> UpdateCookbookAsync(long id, CookbookUpdateModel model)
        {
            var entity = await _db.HouseholdCookbooks
                .Include(c => c.Recipes)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (entity == null) return null;

            if (model.Name != null)
            {
                entity.Name = model.Name;
                entity.Slug = GenerateSlug(model.Name);
            }
            if (model.Description != null) entity.Description = model.Description;
            if (model.IsPublic.HasValue) entity.IsPublic = model.IsPublic.Value;

            await _db.SaveChangesAsync();
            return MapCookbook(entity);
        }

        public async Task<bool> DeleteCookbookAsync(long id)
        {
            var entity = await _db.HouseholdCookbooks.FindAsync(id);
            if (entity == null) return false;

            _db.HouseholdCookbooks.Remove(entity);
            await _db.SaveChangesAsync();
            return true;
        }

        public async Task<bool> AddRecipeToCookbookAsync(long cookbookId, long recipeId)
        {
            var exists = await _db.HouseholdCookbookRecipes
                .AnyAsync(cr => cr.HouseholdCookbookId == cookbookId && cr.RecipeId == recipeId);

            if (exists) return false;

            _db.HouseholdCookbookRecipes.Add(new HouseholdCookbookRecipeEntity
            {
                HouseholdCookbookId = cookbookId,
                RecipeId = recipeId
            });
            await _db.SaveChangesAsync();
            return true;
        }

        public async Task<bool> RemoveRecipeFromCookbookAsync(long cookbookId, long recipeId)
        {
            var entry = await _db.HouseholdCookbookRecipes
                .FirstOrDefaultAsync(cr => cr.HouseholdCookbookId == cookbookId && cr.RecipeId == recipeId);

            if (entry == null) return false;

            _db.HouseholdCookbookRecipes.Remove(entry);
            await _db.SaveChangesAsync();
            return true;
        }

        public async Task<List<RecipeResponseModel>> GetCookbookRecipesAsync(long cookbookId)
        {
            return await _db.HouseholdCookbookRecipes
                .Where(cr => cr.HouseholdCookbookId == cookbookId)
                .Include(cr => cr.Recipe)
                .AsNoTracking()
                .Select(cr => new RecipeResponseModel
                {
                    Id = cr.Recipe!.Id,
                    Name = cr.Recipe.Name,
                    Description = cr.Recipe.Description ?? string.Empty,
                    AuthorId = cr.Recipe.AuthorId,
                    ImageUrl = cr.Recipe.Image,
                    PrepTimeMinutes = cr.Recipe.PrepTimeMinutes,
                    CookTimeMinutes = cr.Recipe.CookTimeMinutes,
                    Servings = cr.Recipe.Servings,
                    CreatedDate = cr.Recipe.CreatedDate
                })
                .ToListAsync();
        }

        private static CookbookResponseModel MapCookbook(HouseholdCookbookEntity entity)
        {
            return new CookbookResponseModel
            {
                Id = entity.Id,
                HouseholdId = entity.HouseholdId,
                Name = entity.Name,
                Description = entity.Description,
                Slug = entity.Slug,
                IsPublic = entity.IsPublic,
                RecipeCount = entity.Recipes?.Count ?? 0,
                CreatedDate = entity.CreatedDate
            };
        }

        private static string GenerateSlug(string name)
        {
            return name.ToLowerInvariant()
                .Replace(" ", "-")
                .Replace("'", "")
                .Replace("\"", "");
        }
    }
}

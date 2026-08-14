using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nom.Data;
using Nom.Data.Recipe;
using Nom.Orch.Extensions;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Recipe;

namespace Nom.Orch.Services
{
    public class DishGroupService : IDishGroupService
    {
        private readonly ApplicationDbContext _db;
        private readonly ILogger<DishGroupService> _logger;

        public DishGroupService(ApplicationDbContext db, ILogger<DishGroupService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public static string Slugify(string name) =>
            Regex.Replace(name.Trim().ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');

        public async Task<DishGroupModel> GetOrCreateAsync(string canonicalName)
        {
            var name = canonicalName.Trim().ToLowerInvariant();
            var slug = Slugify(name);
            if (slug.Length == 0)
            {
                throw new ArgumentException("Not a usable dish name.", nameof(canonicalName));
            }

            var existing = await _db.DishGroups.FirstOrDefaultAsync(g => g.Slug == slug);
            if (existing != null)
            {
                return Map(existing, await CountAsync(existing.Id));
            }

            var group = new DishGroupEntity
            {
                Name = name,
                Slug = slug,
                CreatedDate = DateTime.UtcNow,
            };
            _db.DishGroups.Add(group);
            await _db.SaveChangesAsync();
            _logger.LogInformation("Created dish group '{Name}'", name);
            return Map(group, 0);
        }

        public async Task<List<DishGroupModel>> ListAsync(int limit = 200)
        {
            return await _db.DishGroups
                .AsNoTracking()
                .Select(g => new DishGroupModel
                {
                    Id = g.Id,
                    Name = g.Name,
                    Slug = g.Slug,
                    RecipeCount = g.Recipes.Count(r => !r.IsDeleted),
                })
                .OrderByDescending(g => g.RecipeCount)
                .Take(Math.Clamp(limit, 1, 1000))
                .ToListAsync();
        }

        public async Task<DishGroupDetailModel?> GetBySlugAsync(string slug, long? viewerPersonId)
        {
            var group = await _db.DishGroups.AsNoTracking().FirstOrDefaultAsync(g => g.Slug == slug);
            if (group == null)
            {
                return null;
            }

            var recipes = await _db.Recipes
                .AsNoTracking()
                .Where(r => r.DishGroupId == group.Id)
                .VisibleTo(_db, viewerPersonId)
                .Select(r => new DishGroupRecipeModel
                {
                    Id = r.Id,
                    Name = r.Name,
                    Image = r.Image,
                    Rating = r.Rating,
                })
                .OrderBy(r => r.Name)
                .ToListAsync();

            return new DishGroupDetailModel
            {
                Id = group.Id,
                Name = group.Name,
                Slug = group.Slug,
                RecipeCount = recipes.Count,
                Recipes = recipes,
            };
        }

        public async Task<bool> AssignAsync(long recipeId, long? dishGroupId)
        {
            var recipe = await _db.Recipes.FirstOrDefaultAsync(r => r.Id == recipeId);
            if (recipe == null)
            {
                return false;
            }

            if (dishGroupId.HasValue && !await _db.DishGroups.AnyAsync(g => g.Id == dishGroupId.Value))
            {
                return false;
            }

            recipe.DishGroupId = dishGroupId;
            recipe.LastModifiedDate = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return true;
        }

        public async Task<bool> MergeAsync(long sourceGroupId, long targetGroupId, long adminPersonId)
        {
            if (sourceGroupId == targetGroupId)
            {
                return false;
            }

            var source = await _db.DishGroups.FirstOrDefaultAsync(g => g.Id == sourceGroupId);
            var target = await _db.DishGroups.FirstOrDefaultAsync(g => g.Id == targetGroupId);
            if (source == null || target == null)
            {
                return false;
            }

            await _db.Recipes
                .Where(r => r.DishGroupId == sourceGroupId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.DishGroupId, targetGroupId)
                    .SetProperty(r => r.LastModifiedDate, DateTime.UtcNow));

            source.IsDeleted = true;
            source.DeletedAt = DateTime.UtcNow;
            source.DeletedByPersonId = adminPersonId;
            await _db.SaveChangesAsync();

            _logger.LogInformation("Merged dish group '{Source}' into '{Target}' by person {PersonId}",
                source.Name, target.Name, adminPersonId);
            return true;
        }

        private async Task<int> CountAsync(long groupId) =>
            await _db.Recipes.CountAsync(r => r.DishGroupId == groupId && !r.IsDeleted);

        private static DishGroupModel Map(DishGroupEntity g, int count) => new()
        {
            Id = g.Id,
            Name = g.Name,
            Slug = g.Slug,
            RecipeCount = count,
        };
    }
}

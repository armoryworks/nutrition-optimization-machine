// File: nom-api/Nom.Orch/Services/DietAdminOrchestrationService.cs

using Microsoft.EntityFrameworkCore;
using Nom.Data;
using Nom.Data.Plan;
using Nom.Data.Reference;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Plan;

namespace Nom.Orch.Services
{
    public class DietAdminOrchestrationService : IDietAdminOrchestrationService
    {
        // The reference groups that constitute "diet categories". Kept by name so
        // instance databases with different ids still resolve.
        private static readonly string[] DietGroupNames =
        {
            "Restriction Types",
            "Diets & Eating Patterns",
            "Allergies & Intolerances",
            "Medical Conditions",
            "Religious & Cultural",
        };

        private readonly ApplicationDbContext _context;

        public DietAdminOrchestrationService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<List<RestrictionGroupModel>> GetGroupsAsync()
        {
            var groups = await _context.Set<ReferenceGroupEntity>()
                .AsNoTracking()
                .Where(g => DietGroupNames.Contains(g.Name))
                .Include(g => g.References)
                .OrderBy(g => g.Name)
                .ToListAsync();

            var criteriaCounts = await _context.Set<RestrictionCriterionEntity>()
                .GroupBy(c => c.RestrictionTypeId)
                .Select(g => new { g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Key, x => x.Count);

            return groups.Select(g => new RestrictionGroupModel
            {
                Id = g.Id,
                Name = g.Name,
                Description = g.Description,
                Categories = (g.References ?? Enumerable.Empty<ReferenceEntity>())
                    .Where(r => !r.IsDeleted)
                    .OrderBy(r => r.Name)
                    .Select(r => new RestrictionCategoryModel
                    {
                        Id = r.Id,
                        Name = r.Name,
                        Description = r.Description,
                        CriteriaCount = criteriaCounts.GetValueOrDefault(r.Id),
                    }).ToList(),
            }).ToList();
        }

        public async Task<RestrictionCategoryModel?> CreateCategoryAsync(CreateRestrictionCategoryRequest request)
        {
            var group = await _context.Set<ReferenceGroupEntity>()
                .Include(g => g.References)
                .FirstOrDefaultAsync(g => g.Id == request.GroupId && DietGroupNames.Contains(g.Name));
            if (group == null)
                return null;

            var reference = new ReferenceEntity
            {
                Name = request.Name.Trim(),
                Description = request.Description?.Trim(),
            };
            group.References ??= new List<ReferenceEntity>();
            group.References.Add(reference);
            await _context.SaveChangesAsync();

            return new RestrictionCategoryModel
            {
                Id = reference.Id,
                Name = reference.Name,
                Description = reference.Description,
                CriteriaCount = 0,
            };
        }

        public async Task<RestrictionCategoryModel?> UpdateCategoryAsync(long id, UpdateRestrictionCategoryRequest request)
        {
            var reference = await FindDietCategoryAsync(id);
            if (reference == null)
                return null;

            reference.Name = request.Name.Trim();
            reference.Description = request.Description?.Trim();
            await _context.SaveChangesAsync();

            var count = await _context.Set<RestrictionCriterionEntity>().CountAsync(c => c.RestrictionTypeId == id);
            return new RestrictionCategoryModel
            {
                Id = reference.Id,
                Name = reference.Name,
                Description = reference.Description,
                CriteriaCount = count,
            };
        }

        public async Task<bool?> DeleteCategoryAsync(long id)
        {
            var reference = await FindDietCategoryAsync(id);
            if (reference == null)
                return null;

            var inUse = await _context.Set<RestrictionEntity>().AnyAsync(r => r.RestrictionTypeId == id);
            if (inUse)
                return false;

            var criteria = await _context.Set<RestrictionCriterionEntity>()
                .Where(c => c.RestrictionTypeId == id).ToListAsync();
            _context.RemoveRange(criteria);
            reference.Groups?.Clear();   // drops the ReferenceIndex join rows
            _context.Remove(reference);
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<List<RestrictionCriterionModel>> GetCriteriaAsync(long categoryId)
        {
            return await _context.Set<RestrictionCriterionEntity>()
                .AsNoTracking()
                .Where(c => c.RestrictionTypeId == categoryId)
                .OrderByDescending(c => c.Severity).ThenBy(c => c.Id)
                .Select(c => new RestrictionCriterionModel
                {
                    Id = c.Id,
                    RestrictionTypeId = c.RestrictionTypeId,
                    IngredientId = c.IngredientId,
                    IngredientName = c.Ingredient != null ? c.Ingredient.Name : null,
                    IngredientPattern = c.IngredientPattern,
                    NutrientId = c.NutrientId,
                    NutrientName = c.Nutrient != null ? c.Nutrient.Name : null,
                    MaxAmountPerServing = c.MaxAmountPerServing,
                    Severity = c.Severity,
                    Notes = c.Notes,
                })
                .ToListAsync();
        }

        public async Task<RestrictionCriterionModel?> AddCriterionAsync(long categoryId, SaveRestrictionCriterionRequest request)
        {
            if (request.IngredientId == null
                && string.IsNullOrWhiteSpace(request.IngredientPattern)
                && request.NutrientId == null)
                return null;

            var reference = await FindDietCategoryAsync(categoryId);
            if (reference == null)
                return null;

            var entity = new RestrictionCriterionEntity
            {
                RestrictionTypeId = categoryId,
                IngredientId = request.IngredientId,
                IngredientPattern = string.IsNullOrWhiteSpace(request.IngredientPattern) ? null : request.IngredientPattern.Trim(),
                NutrientId = request.NutrientId,
                MaxAmountPerServing = request.MaxAmountPerServing,
                Severity = request.Severity,
                Notes = request.Notes?.Trim(),
            };
            _context.Add(entity);
            await _context.SaveChangesAsync();

            return (await GetCriteriaAsync(categoryId)).First(c => c.Id == entity.Id);
        }

        public async Task<bool> DeleteCriterionAsync(long criterionId)
        {
            var entity = await _context.Set<RestrictionCriterionEntity>().FindAsync(criterionId);
            if (entity == null)
                return false;
            _context.Remove(entity);
            await _context.SaveChangesAsync();
            return true;
        }

        /// <summary>The reference row, but only if it belongs to one of the diet groups.</summary>
        private async Task<ReferenceEntity?> FindDietCategoryAsync(long id)
        {
            return await _context.Set<ReferenceEntity>()
                .Include(r => r.Groups)
                .FirstOrDefaultAsync(r => r.Id == id
                    && r.Groups!.Any(g => DietGroupNames.Contains(g.Name)));
        }
    }
}

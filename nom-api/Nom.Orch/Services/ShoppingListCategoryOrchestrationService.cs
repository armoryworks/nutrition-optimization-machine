using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nom.Data;
using Nom.Data.Shopping;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Shopping;

namespace Nom.Orch.Services
{
    public class ShoppingListCategoryOrchestrationService : IShoppingListCategoryOrchestrationService
    {
        private readonly ApplicationDbContext _context;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ILogger<ShoppingListCategoryOrchestrationService> _logger;

        public ShoppingListCategoryOrchestrationService(
            ApplicationDbContext context,
            IHttpContextAccessor httpContextAccessor,
            ILogger<ShoppingListCategoryOrchestrationService> logger)
        {
            _context = context;
            _httpContextAccessor = httpContextAccessor;
            _logger = logger;
        }

        public async Task<List<ShoppingListCategoryResponseModel>> GetAllCategoriesAsync()
        {
            var categories = await _context.ShoppingListCategories
                .Include(c => c.Household)
                .Include(c => c.Items)
                .OrderBy(c => c.SortOrder)
                .ThenBy(c => c.Name)
                .ToListAsync();

            return categories.Select(c => new ShoppingListCategoryResponseModel
            {
                Id = c.Id,
                Name = c.Name,
                Description = c.Description,
                HouseholdId = c.HouseholdId,
                HouseholdName = c.Household.Name,
                SortOrder = c.SortOrder,
                Color = c.Color,
                ItemCount = c.Items.Count,
                CreatedDate = c.CreatedDate,
                LastModifiedDate = c.LastModifiedDate
            }).ToList();
        }

        public async Task<ShoppingListCategoryResponseModel?> GetCategoryAsync(long id)
        {
            var category = await _context.ShoppingListCategories
                .Include(c => c.Household)
                .Include(c => c.Items)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (category == null) return null;

            return new ShoppingListCategoryResponseModel
            {
                Id = category.Id,
                Name = category.Name,
                Description = category.Description,
                HouseholdId = category.HouseholdId,
                HouseholdName = category.Household.Name,
                SortOrder = category.SortOrder,
                Color = category.Color,
                ItemCount = category.Items.Count,
                CreatedDate = category.CreatedDate,
                LastModifiedDate = category.LastModifiedDate
            };
        }

        public async Task<ShoppingListCategoryResponseModel> CreateCategoryAsync(ShoppingListCategoryCreateModel model)
        {
            var category = new ShoppingListCategoryEntity
            {
                Name = model.Name,
                Description = model.Description,
                SortOrder = model.SortOrder,
                Color = model.Color,
                CreatedDate = DateTime.UtcNow,
                LastModifiedDate = DateTime.UtcNow
            };

            _context.ShoppingListCategories.Add(category);
            await _context.SaveChangesAsync();

            return await GetCategoryAsync(category.Id) ?? throw new InvalidOperationException("Failed to create category");
        }

        public async Task<ShoppingListCategoryResponseModel?> UpdateCategoryAsync(long id, ShoppingListCategoryCreateModel model)
        {
            var category = await _context.ShoppingListCategories.FindAsync(id);
            if (category == null) return null;

            category.Name = model.Name;
            category.Description = model.Description;
            category.SortOrder = model.SortOrder;
            category.Color = model.Color;
            category.LastModifiedDate = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return await GetCategoryAsync(id);
        }

        public async Task<bool> DeleteCategoryAsync(long id)
        {
            var category = await _context.ShoppingListCategories.FindAsync(id);
            if (category == null) return false;

            // Move all items to uncategorized (null category)
            var items = await _context.ShoppingListItems
                .Where(i => i.CategoryId == id)
                .ToListAsync();

            foreach (var item in items)
            {
                item.CategoryId = null;
            }

            _context.ShoppingListCategories.Remove(category);
            await _context.SaveChangesAsync();

            return true;
        }

        public async Task<bool> MoveItemsToCategoryAsync(ShoppingListBulkOperationModel model)
        {
            if (!model.ItemIds.Any()) return false;

            var items = await _context.ShoppingListItems
                .Where(i => model.ItemIds.Contains(i.Id))
                .ToListAsync();

            if (!items.Any()) return false;

            switch (model.Operation.ToLower())
            {
                case "move":
                    if (model.TargetCategoryId.HasValue)
                    {
                        foreach (var item in items)
                        {
                            item.CategoryId = model.TargetCategoryId.Value;
                            item.LastModifiedDate = DateTime.UtcNow;
                        }
                    }
                    break;

                case "complete":
                    foreach (var item in items)
                    {
                        item.IsChecked = true;
                        item.LastModifiedDate = DateTime.UtcNow;
                    }
                    break;

                case "delete":
                    _context.ShoppingListItems.RemoveRange(items);
                    break;

                default:
                    return false;
            }

            await _context.SaveChangesAsync();
            return true;
        }
    }
} 
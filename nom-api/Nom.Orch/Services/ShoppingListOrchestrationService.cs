// File: Nom.Orch/Services/ShoppingListOrchestrationService.cs

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nom.Data;
using Nom.Data.Person;
using Nom.Data.Plan;
using Nom.Data.Recipe;
using Nom.Data.Reference;
using Nom.Data.Shopping;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Shopping;

namespace Nom.Orch.Services
{
    public class ShoppingListOrchestrationService : IShoppingListOrchestrationService
    {
        private readonly ApplicationDbContext _context;

        public ShoppingListOrchestrationService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<List<ShoppingListResponseModel>> GetAllShoppingListsAsync()
        {
            var shoppingLists = await _context.ShoppingLists
                .Include(sl => sl.Items)
                .ToListAsync();

            return shoppingLists.Select(sl => new ShoppingListResponseModel
            {
                Id = sl.Id,
                Name = sl.Name,
                Description = sl.Description,
                AuthorId = sl.AuthorId,
                HouseholdId = sl.HouseholdId,
                ShoppingListGroupId = sl.ShoppingListGroupId,
                ItemCount = sl.Items?.Count ?? 0,
                CompletedItemCount = sl.Items?.Count(i => i.IsChecked) ?? 0,
                CreatedDate = sl.CreatedDate,
                ModifiedDate = sl.LastModifiedDate
            }).ToList();
        }

        public async Task<ShoppingListCreateResponseModel> CreateShoppingListAsync(ShoppingListCreateModel model, long authorId)
        {
            var shoppingList = new ShoppingListEntity
            {
                Name = model.Name,
                Description = model.Description,
                AuthorId = authorId,
                HouseholdId = model.HouseholdId,
                ShoppingListGroupId = model.ShoppingListGroupId,
                CreatedDate = DateTime.UtcNow,
                LastModifiedDate = DateTime.UtcNow
            };

            _context.ShoppingLists.Add(shoppingList);
            await _context.SaveChangesAsync();

            return new ShoppingListCreateResponseModel
            {
                Id = shoppingList.Id,
                Name = shoppingList.Name,
                Description = shoppingList.Description,
                AuthorId = shoppingList.AuthorId,
                HouseholdId = shoppingList.HouseholdId,
                ShoppingListGroupId = shoppingList.ShoppingListGroupId,
                CreatedDate = shoppingList.CreatedDate
            };
        }

        public async Task<ShoppingListResponseModel?> GetShoppingListAsync(long id)
        {
            var shoppingList = await _context.ShoppingLists
                .Include(sl => sl.Items)
                .FirstOrDefaultAsync(sl => sl.Id == id);

            if (shoppingList == null)
                return null;

            return new ShoppingListResponseModel
            {
                Id = shoppingList.Id,
                Name = shoppingList.Name,
                Description = shoppingList.Description,
                AuthorId = shoppingList.AuthorId,
                HouseholdId = shoppingList.HouseholdId,
                ShoppingListGroupId = shoppingList.ShoppingListGroupId,
                ItemCount = shoppingList.Items?.Count ?? 0,
                CompletedItemCount = shoppingList.Items?.Count(i => i.IsChecked) ?? 0,
                CreatedDate = shoppingList.CreatedDate,
                ModifiedDate = shoppingList.LastModifiedDate
            };
        }

        public async Task<ShoppingListResponseModel?> UpdateShoppingListAsync(long id, ShoppingListUpdateModel model)
        {
            var shoppingList = await _context.ShoppingLists.FindAsync(id);
            if (shoppingList == null)
                return null;

            shoppingList.Name = model.Name;
            shoppingList.Description = model.Description;
            shoppingList.HouseholdId = model.HouseholdId;
            shoppingList.ShoppingListGroupId = model.ShoppingListGroupId;
            shoppingList.LastModifiedDate = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return new ShoppingListResponseModel
            {
                Id = shoppingList.Id,
                Name = shoppingList.Name,
                Description = shoppingList.Description,
                AuthorId = shoppingList.AuthorId,
                HouseholdId = shoppingList.HouseholdId,
                ShoppingListGroupId = shoppingList.ShoppingListGroupId,
                ItemCount = 0, // Would need to load items to get count
                CompletedItemCount = 0, // Would need to load items to get count
                CreatedDate = shoppingList.CreatedDate,
                ModifiedDate = shoppingList.LastModifiedDate
            };
        }

        public async Task<bool> DeleteShoppingListAsync(long id)
        {
            var shoppingList = await _context.ShoppingLists.FindAsync(id);
            if (shoppingList == null)
                return false;

            _context.ShoppingLists.Remove(shoppingList);
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<ShoppingListItemResponseModel> AddItemAsync(ShoppingListItemCreateModel model)
        {
            var item = new ShoppingListItemEntity
            {
                ShoppingListId = model.ShoppingListId,
                Name = model.Name,
                Quantity = model.Quantity,
                Note = model.Note,
                IngredientId = model.IngredientId,
                RecipeId = model.RecipeId,
                Position = model.Position,
                CreatedDate = DateTime.UtcNow,
                LastModifiedDate = DateTime.UtcNow
            };

            _context.ShoppingListItems.Add(item);
            await _context.SaveChangesAsync();

            return new ShoppingListItemResponseModel
            {
                Id = item.Id,
                ShoppingListId = item.ShoppingListId,
                Name = item.Name,
                Quantity = item.Quantity,
                IsCompleted = item.IsChecked,
                Note = item.Note,
                IngredientId = item.IngredientId,
                RecipeId = item.RecipeId,
                Position = item.Position,
                CreatedDate = item.CreatedDate,
                ModifiedDate = item.LastModifiedDate
            };
        }

        public async Task<ShoppingListItemResponseModel?> UpdateItemAsync(long id, ShoppingListItemUpdateModel model)
        {
            var item = await _context.ShoppingListItems.FindAsync(id);
            if (item == null)
                return null;

            item.Name = model.Name;
            item.Quantity = model.Quantity;
            item.IsChecked = model.IsCompleted;
            item.Note = model.Note;
            item.Position = model.Position;
            item.LastModifiedDate = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return new ShoppingListItemResponseModel
            {
                Id = item.Id,
                ShoppingListId = item.ShoppingListId,
                Name = item.Name,
                Quantity = item.Quantity,
                IsCompleted = item.IsChecked,
                Note = item.Note,
                IngredientId = item.IngredientId,
                RecipeId = item.RecipeId,
                Position = item.Position,
                CreatedDate = item.CreatedDate,
                ModifiedDate = item.LastModifiedDate
            };
        }

        public async Task<bool> DeleteItemAsync(long id)
        {
            var item = await _context.ShoppingListItems.FindAsync(id);
            if (item == null)
                return false;

            _context.ShoppingListItems.Remove(item);
            await _context.SaveChangesAsync();
            return true;
        }

        // Recipe Integration Implementation
        public async Task<ShoppingListResponseModel> AddRecipeIngredientsAsync(ShoppingListRecipeAddModel model)
        {
            // Get the recipe with its ingredients
            var recipe = await _context.Recipes
                .Include(r => r.RecipeIngredients)
                .ThenInclude(ri => ri.Ingredient)
                .FirstOrDefaultAsync(r => r.Id == model.RecipeId);

            if (recipe == null)
                throw new InvalidOperationException($"Recipe with ID {model.RecipeId} not found");

            var shoppingList = await _context.ShoppingLists
                .Include(sl => sl.Items)
                .FirstOrDefaultAsync(sl => sl.Id == model.ShoppingListId);

            if (shoppingList == null)
                throw new InvalidOperationException($"Shopping list with ID {model.ShoppingListId} not found");

            // Get existing items to avoid duplicates
            var existingItems = shoppingList.Items?.ToList() ?? new List<ShoppingListItemEntity>();

            // Add recipe ingredients as shopping list items
            var ingredientsToAdd = model.IncludeAllIngredients 
                ? recipe.RecipeIngredients 
                : recipe.RecipeIngredients.Where(ri => model.SelectedIngredientIds?.Contains(ri.IngredientId) == true);

            foreach (var recipeIngredient in ingredientsToAdd)
            {
                // Check if item already exists
                var existingItem = existingItems.FirstOrDefault(i => 
                    i.IngredientId == recipeIngredient.IngredientId && 
                    i.RecipeId == model.RecipeId);

                if (existingItem == null)
                {
                    var scaledQuantity = recipeIngredient.Quantity * (model.ScaleFactor ?? 1.0m);
                    
                    var shoppingListItem = new ShoppingListItemEntity
                    {
                        ShoppingListId = model.ShoppingListId,
                        Name = recipeIngredient.Ingredient?.Name ?? "Unknown Ingredient",
                        Quantity = scaledQuantity,
                        Note = $"From recipe: {recipe.Name}",
                        IngredientId = recipeIngredient.IngredientId,
                        RecipeId = model.RecipeId,
                        Position = existingItems.Count + 1,
                        CreatedDate = DateTime.UtcNow,
                        LastModifiedDate = DateTime.UtcNow
                    };

                    _context.ShoppingListItems.Add(shoppingListItem);
                }
            }

            await _context.SaveChangesAsync();

            // Return updated shopping list
            return await GetShoppingListAsync(model.ShoppingListId) ?? 
                throw new InvalidOperationException("Failed to retrieve updated shopping list");
        }

        public async Task<ShoppingListResponseModel> RemoveRecipeIngredientsAsync(ShoppingListRecipeRemoveModel model)
        {
            var shoppingList = await _context.ShoppingLists
                .Include(sl => sl.Items)
                .FirstOrDefaultAsync(sl => sl.Id == model.ShoppingListId);

            if (shoppingList == null)
                throw new InvalidOperationException($"Shopping list with ID {model.ShoppingListId} not found");

            var itemsToRemove = model.RemoveAllIngredients
                ? shoppingList.Items?.Where(i => i.RecipeId == model.RecipeId).ToList()
                : shoppingList.Items?.Where(i => i.RecipeId == model.RecipeId && 
                    model.SelectedIngredientIds?.Contains(i.IngredientId ?? 0) == true).ToList();

            if (itemsToRemove?.Any() == true)
            {
                _context.ShoppingListItems.RemoveRange(itemsToRemove);
                await _context.SaveChangesAsync();
            }

            // Return updated shopping list
            return await GetShoppingListAsync(model.ShoppingListId) ?? 
                throw new InvalidOperationException("Failed to retrieve updated shopping list");
        }
    }
} 
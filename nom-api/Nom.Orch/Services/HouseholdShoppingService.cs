using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Nom.Data;
using Nom.Data.Recipe;
using Nom.Data.Shopping;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Shopping;

namespace Nom.Orch.Services
{
    public class HouseholdShoppingService : IHouseholdShoppingService
    {
        public const string CheckTtlDaysKey = "Shopping:CheckTtlDays";
        public const int DefaultCheckTtlDays = 3;
        public const string AdHocKeyPrefix = "adhoc:";

        private readonly ApplicationDbContext _context;
        private readonly IIngredientVerificationService _verifications;
        private readonly ILogger<HouseholdShoppingService> _logger;
        private readonly TimeSpan _checkTtl;

        public HouseholdShoppingService(
            ApplicationDbContext context,
            IConfiguration configuration,
            IIngredientVerificationService verifications,
            ILogger<HouseholdShoppingService> logger)
        {
            _context = context;
            _verifications = verifications;
            _logger = logger;
            var days = configuration.GetValue<int?>(CheckTtlDaysKey) ?? DefaultCheckTtlDays;
            _checkTtl = TimeSpan.FromDays(Math.Max(1, days));
        }

        public async Task<ShoppingStateModel> GetStateAsync(long householdId)
        {
            var items = await _context.HouseholdShoppingItems
                .AsNoTracking()
                .Include(i => i.Ingredient!).ThenInclude(i => i.Label)
                .Where(i => i.HouseholdId == householdId)
                .OrderBy(i => i.CreatedDate).ThenBy(i => i.Id)
                .ToListAsync();

            var linked = items.Where(i => i.IngredientId != null).Select(i => i.IngredientId!.Value).Distinct().ToList();
            var needs = linked.Count == 0
                ? new Dictionary<long, bool>()
                : (await _verifications.NeedsVerificationAsync(householdId, linked)).ToDictionary(n => n.IngredientId, n => n.ShouldAsk);

            var cutoff = DateTime.UtcNow - _checkTtl;
            var checks = await _context.HouseholdShoppingChecks
                .Where(c => c.HouseholdId == householdId && c.Checked)
                .ToListAsync();
            var expired = checks.Where(c => c.CheckedDate == null || c.CheckedDate < cutoff).ToList();
            if (expired.Count > 0)
            {
                foreach (var c in expired)
                {
                    c.Checked = false;
                    c.LastModifiedDate = DateTime.UtcNow;
                }
                await _context.SaveChangesAsync();
            }

            return new ShoppingStateModel
            {
                Items = items.Select(i => ToModel(i, needs)).ToList(),
                Checks = checks.Except(expired).Select(c => new ShoppingCheckModel
                {
                    ItemKey = c.ItemKey,
                    CheckedDate = c.CheckedDate!.Value,
                    CheckedByPersonId = c.CheckedByPersonId,
                }).ToList(),
            };
        }

        public async Task<ShoppingItemAddResult?> AddItemAsync(long householdId, long personId, HouseholdShoppingItemCreateModel model)
        {
            var name = model.Name.Trim();
            var lowered = name.ToLower();

            var existing = await _context.HouseholdShoppingItems
                .Include(i => i.Ingredient!).ThenInclude(i => i.Label)
                .FirstOrDefaultAsync(i => i.HouseholdId == householdId && i.Name.ToLower() == lowered);
            if (existing != null)
                return new ShoppingItemAddResult { Item = await WithVerificationAsync(existing), Created = false };

            long? ingredientId;
            if (model.IngredientId is { } requested)
            {
                if (!await _context.Ingredients.AnyAsync(i => i.Id == requested))
                    return null;
                ingredientId = requested;
            }
            else
            {
                ingredientId = await ResolveOrCreateIngredientAsync(name, lowered, personId);
            }

            var now = DateTime.UtcNow;
            var item = new HouseholdShoppingItemEntity
            {
                HouseholdId = householdId,
                Name = name,
                IngredientId = ingredientId,
                Quantity = model.Quantity,
                Unit = Clip(model.Unit, 32),
                CreatedDate = now,
                CreatedByPersonId = personId,
            };
            _context.HouseholdShoppingItems.Add(item);
            await _context.SaveChangesAsync();

            var saved = await _context.HouseholdShoppingItems
                .Include(i => i.Ingredient!).ThenInclude(i => i.Label)
                .FirstAsync(i => i.Id == item.Id);
            return new ShoppingItemAddResult { Item = await WithVerificationAsync(saved), Created = true };
        }

        public async Task<HouseholdShoppingItemModel?> GetItemAsync(long id)
        {
            var item = await _context.HouseholdShoppingItems.AsNoTracking()
                .Include(i => i.Ingredient!).ThenInclude(i => i.Label)
                .FirstOrDefaultAsync(i => i.Id == id);
            return item == null ? null : await WithVerificationAsync(item);
        }

        public async Task<HouseholdShoppingItemModel?> UpdateItemAsync(long id, long personId, HouseholdShoppingItemUpdateModel model)
        {
            var item = await _context.HouseholdShoppingItems
                .Include(i => i.Ingredient!).ThenInclude(i => i.Label)
                .FirstOrDefaultAsync(i => i.Id == id);
            if (item == null) return null;

            if (model.IngredientId is { } requested && !await _context.Ingredients.AnyAsync(i => i.Id == requested))
                throw new ArgumentException("That ingredient does not exist.");

            if (!string.IsNullOrWhiteSpace(model.Name)) item.Name = model.Name.Trim();
            item.Quantity = model.Quantity;
            item.Unit = Clip(model.Unit, 32);
            item.IngredientId = model.IngredientId;
            item.LastModifiedDate = DateTime.UtcNow;
            item.LastModifiedByPersonId = personId;
            await _context.SaveChangesAsync();

            var saved = await _context.HouseholdShoppingItems
                .Include(i => i.Ingredient!).ThenInclude(i => i.Label)
                .FirstAsync(i => i.Id == id);
            return await WithVerificationAsync(saved);
        }

        public async Task<bool> DeleteItemAsync(long id, long personId)
        {
            var item = await _context.HouseholdShoppingItems.FirstOrDefaultAsync(i => i.Id == id);
            if (item == null) return false;
            item.IsDeleted = true;
            item.DeletedAt = DateTime.UtcNow;
            item.DeletedByPersonId = personId;
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<ShoppingCheckResultModel?> SetCheckAsync(long householdId, long personId, ShoppingCheckRequestModel request)
        {
            var hasKey = !string.IsNullOrWhiteSpace(request.ItemKey);
            if (hasKey == request.ItemId.HasValue)
                throw new ArgumentException("Send exactly one of itemKey or itemId.");

            var now = DateTime.UtcNow;
            var result = new ShoppingCheckResultModel { Checked = request.Checked };
            long? ingredientId = null;

            if (request.ItemId is { } itemId)
            {
                var item = await _context.HouseholdShoppingItems.FirstOrDefaultAsync(i => i.Id == itemId && i.HouseholdId == householdId);
                if (item == null) return null;
                item.Checked = request.Checked;
                item.CheckedDate = request.Checked ? now : null;
                item.CheckedByPersonId = request.Checked ? personId : null;
                item.LastModifiedDate = now;
                item.LastModifiedByPersonId = personId;
                result.ItemId = item.Id;
                result.ItemKey = AdHocKeyPrefix + item.Id;
                result.CheckedDate = item.CheckedDate;
                ingredientId = item.IngredientId;
            }
            else
            {
                var key = request.ItemKey!.Trim();
                var row = await _context.HouseholdShoppingChecks.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(c => c.HouseholdId == householdId && c.ItemKey == key);
                if (row == null)
                {
                    row = new HouseholdShoppingCheckEntity
                    {
                        HouseholdId = householdId,
                        ItemKey = key,
                        CreatedDate = now,
                        CreatedByPersonId = personId,
                    };
                    _context.HouseholdShoppingChecks.Add(row);
                }
                row.IsDeleted = false;
                row.DeletedAt = null;
                row.DeletedByPersonId = null;
                row.Checked = request.Checked;
                row.CheckedDate = request.Checked ? now : null;
                row.CheckedByPersonId = request.Checked ? personId : null;
                row.LastModifiedDate = now;
                row.LastModifiedByPersonId = personId;
                result.ItemKey = key;
                result.CheckedDate = row.CheckedDate;
                if (long.TryParse(key, out var parsed)) ingredientId = parsed;
            }

            await _context.SaveChangesAsync();

            if (request.Checked && ingredientId is { } id)
            {
                var needs = (await _verifications.NeedsVerificationAsync(householdId, new[] { id })).FirstOrDefault();
                if (needs is { ShouldAsk: true }) result.Verification = needs;
            }
            return result;
        }

        public async Task ClearChecksAsync(long householdId, long personId)
        {
            var now = DateTime.UtcNow;
            var checks = await _context.HouseholdShoppingChecks.Where(c => c.HouseholdId == householdId && c.Checked).ToListAsync();
            foreach (var c in checks)
            {
                c.Checked = false;
                c.CheckedDate = null;
                c.CheckedByPersonId = null;
                c.LastModifiedDate = now;
                c.LastModifiedByPersonId = personId;
            }
            var items = await _context.HouseholdShoppingItems.Where(i => i.HouseholdId == householdId && i.Checked).ToListAsync();
            foreach (var i in items)
            {
                i.Checked = false;
                i.CheckedDate = null;
                i.CheckedByPersonId = null;
                i.LastModifiedDate = now;
                i.LastModifiedByPersonId = personId;
            }
            if (checks.Count > 0 || items.Count > 0)
                await _context.SaveChangesAsync();
        }

        private async Task<long> ResolveOrCreateIngredientAsync(string name, string lowered, long personId)
        {
            var match = await _context.Ingredients.Where(i => i.Name.ToLower() == lowered).Select(i => i.Id).FirstOrDefaultAsync();
            if (match != 0) return match;

            var now = DateTime.UtcNow;
            var ingredient = new IngredientEntity
            {
                Name = name,
                FdcDataType = "Custom",
                CurationStatusId = (long)CurationStatusEnum.PendingCuration,
                AuthorId = personId,
                CreatedDate = now,
                CreatedByPersonId = personId,
            };
            _context.Ingredients.Add(ingredient);
            try
            {
                await _context.SaveChangesAsync();
                return ingredient.Id;
            }
            catch (DbUpdateException ex)
            {
                _context.Entry(ingredient).State = EntityState.Detached;
                _logger.LogInformation(ex, "Ingredient '{Name}' appeared while creating it; linking the existing row", name);
                var raced = await _context.Ingredients.IgnoreQueryFilters().Where(i => i.Name.ToLower() == lowered).Select(i => i.Id).FirstOrDefaultAsync();
                if (raced == 0) throw;
                return raced;
            }
        }

        private async Task<HouseholdShoppingItemModel> WithVerificationAsync(HouseholdShoppingItemEntity item)
        {
            var needs = new Dictionary<long, bool>();
            if (item.IngredientId is { } id)
            {
                var n = (await _verifications.NeedsVerificationAsync(item.HouseholdId, new[] { id })).FirstOrDefault();
                if (n != null) needs[id] = n.ShouldAsk;
            }
            return ToModel(item, needs);
        }

        private static HouseholdShoppingItemModel ToModel(HouseholdShoppingItemEntity item, IReadOnlyDictionary<long, bool> needs) => new()
        {
            Id = item.Id,
            HouseholdId = item.HouseholdId,
            Name = item.Name,
            IngredientId = item.IngredientId,
            Quantity = item.Quantity,
            Unit = item.Unit,
            Checked = item.Checked,
            CheckedDate = item.CheckedDate,
            CheckedByPersonId = item.CheckedByPersonId,
            CreatedByPersonId = item.CreatedByPersonId,
            CreatedDate = item.CreatedDate,
            Department = item.Ingredient?.Label?.Name,
            IngredientNeedsVerification = item.IngredientId is { } id && needs.TryGetValue(id, out var ask) && ask,
        };

        private static string? Clip(string? value, int max)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var trimmed = value.Trim();
            return trimmed.Length <= max ? trimmed : trimmed.Substring(0, max);
        }
    }
}

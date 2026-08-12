using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nom.Data;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Recipe;

namespace Nom.Orch.Services
{
    public class RecipeEnhancementService : IRecipeEnhancementService
    {
        private readonly ApplicationDbContext _db;
        private readonly ILogger<RecipeEnhancementService> _logger;

        public RecipeEnhancementService(ApplicationDbContext db, ILogger<RecipeEnhancementService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<List<RecipeSubstitutionModel>> GetSubstitutionsAsync(long recipeId, bool includeUncurated)
        {
            var query = _db.RecipeSubstitutions
                .Include(s => s.SubstituteIngredient)
                .Include(s => s.SubstituteMeasurement)
                .Include(s => s.StepEffects)
                .Where(s => s.RecipeId == recipeId && !s.IsDeleted);

            if (!includeUncurated)
            {
                query = query.Where(s => s.CurationStatusId == (long)CurationStatusEnum.Curated);
            }

            var substitutions = await query.AsNoTracking().ToListAsync();
            return substitutions.Select(MapSubstitution).ToList();
        }

        public async Task<List<RecipeAugmentationModel>> GetAugmentationsAsync(long recipeId, bool includeUncurated)
        {
            var query = _db.RecipeAugmentations
                .Include(a => a.Ingredient)
                .Include(a => a.Measurement)
                .Where(a => a.RecipeId == recipeId && !a.IsDeleted);

            if (!includeUncurated)
            {
                query = query.Where(a => a.CurationStatusId == (long)CurationStatusEnum.Curated);
            }

            var augmentations = await query.AsNoTracking().ToListAsync();
            return augmentations.Select(MapAugmentation).ToList();
        }

        public async Task<RecipeSubstitutionModel> UpsertSubstitutionAsync(
            long recipeId, long? substitutionId, RecipeSubstitutionUpsertModel model, long personId)
        {
            var ingredientBelongs = await _db.RecipeIngredients
                .AnyAsync(ri => ri.RecipeId == recipeId && ri.IngredientId == model.IngredientId);
            if (!ingredientBelongs)
            {
                throw new ArgumentException("IngredientId is not an ingredient of this recipe.");
            }

            var stepNumbers = model.StepEffects.Select(e => e.StepNumber).Distinct().ToList();
            var validStepCount = await _db.RecipeSteps
                .CountAsync(s => s.RecipeId == recipeId && stepNumbers.Contains(s.StepNumber));
            if (validStepCount != stepNumbers.Count)
            {
                throw new ArgumentException("One or more step effects reference step numbers this recipe does not have.");
            }

            RecipeSubstitutionEntity entity;
            if (substitutionId.HasValue)
            {
                entity = await _db.RecipeSubstitutions
                        .Include(s => s.StepEffects)
                        .FirstOrDefaultAsync(s => s.Id == substitutionId.Value && s.RecipeId == recipeId && !s.IsDeleted)
                    ?? throw new KeyNotFoundException("Substitution not found.");

                _db.RecipeSubstitutionStepEffects.RemoveRange(entity.StepEffects);
                entity.LastModifiedDate = DateTime.UtcNow;
                entity.LastModifiedByPersonId = personId;
            }
            else
            {
                entity = new RecipeSubstitutionEntity
                {
                    RecipeId = recipeId,
                    CreatedDate = DateTime.UtcNow,
                    CreatedByPersonId = personId,
                };
                _db.RecipeSubstitutions.Add(entity);
            }

            entity.IngredientId = model.IngredientId;
            entity.SubstituteIngredientId = model.SubstituteIngredientId;
            entity.Ratio = model.Ratio;
            entity.SubstituteQuantity = model.SubstituteQuantity;
            entity.SubstituteMeasurementId = model.SubstituteMeasurementId;
            entity.Notes = model.Notes;
            // Curator-authored — curated immediately.
            entity.CurationStatusId = (long)CurationStatusEnum.Curated;

            entity.StepEffects = model.StepEffects.Select(e => new RecipeSubstitutionStepEffectEntity
            {
                StepNumber = e.StepNumber,
                AlteredDescription = e.AlteredDescription,
                NewTemperatureFahrenheit = e.NewTemperatureFahrenheit,
                DurationDeltaMinutes = e.DurationDeltaMinutes,
                CreatedDate = DateTime.UtcNow,
                CreatedByPersonId = personId,
            }).ToList();

            await _db.SaveChangesAsync();

            return (await GetSubstitutionsAsync(recipeId, includeUncurated: true))
                .First(s => s.Id == entity.Id);
        }

        public async Task<RecipeAugmentationModel> UpsertAugmentationAsync(
            long recipeId, long? augmentationId, RecipeAugmentationUpsertModel model, long personId)
        {
            RecipeAugmentationEntity entity;
            if (augmentationId.HasValue)
            {
                entity = await _db.RecipeAugmentations
                        .FirstOrDefaultAsync(a => a.Id == augmentationId.Value && a.RecipeId == recipeId && !a.IsDeleted)
                    ?? throw new KeyNotFoundException("Augmentation not found.");

                entity.LastModifiedDate = DateTime.UtcNow;
                entity.LastModifiedByPersonId = personId;
            }
            else
            {
                entity = new RecipeAugmentationEntity
                {
                    RecipeId = recipeId,
                    CreatedDate = DateTime.UtcNow,
                    CreatedByPersonId = personId,
                };
                _db.RecipeAugmentations.Add(entity);
            }

            entity.IngredientId = model.IngredientId;
            entity.Quantity = model.Quantity;
            entity.MeasurementId = model.MeasurementId;
            entity.FlavorEffect = model.FlavorEffect;
            entity.Instructions = model.Instructions;
            entity.InsertAfterStepNumber = model.InsertAfterStepNumber;
            entity.NewTemperatureFahrenheit = model.NewTemperatureFahrenheit;
            entity.DurationDeltaMinutes = model.DurationDeltaMinutes;
            entity.CurationStatusId = (long)CurationStatusEnum.Curated;

            await _db.SaveChangesAsync();

            return (await GetAugmentationsAsync(recipeId, includeUncurated: true))
                .First(a => a.Id == entity.Id);
        }

        public async Task<bool> DeleteSubstitutionAsync(long recipeId, long substitutionId, long personId)
        {
            var entity = await _db.RecipeSubstitutions
                .FirstOrDefaultAsync(e => e.Id == substitutionId && e.RecipeId == recipeId && !e.IsDeleted);
            if (entity == null)
            {
                return false;
            }

            SoftDelete(entity, personId);
            await _db.SaveChangesAsync();
            return true;
        }

        public async Task<bool> DeleteAugmentationAsync(long recipeId, long augmentationId, long personId)
        {
            var entity = await _db.RecipeAugmentations
                .FirstOrDefaultAsync(e => e.Id == augmentationId && e.RecipeId == recipeId && !e.IsDeleted);
            if (entity == null)
            {
                return false;
            }

            SoftDelete(entity, personId);
            await _db.SaveChangesAsync();
            return true;
        }

        public async Task<bool> CurateSubstitutionAsync(long recipeId, long substitutionId, long personId)
        {
            var entity = await _db.RecipeSubstitutions
                .FirstOrDefaultAsync(e => e.Id == substitutionId && e.RecipeId == recipeId && !e.IsDeleted);
            if (entity == null)
            {
                return false;
            }

            Curate(personId, () => entity.CurationStatusId = (long)CurationStatusEnum.Curated, entity);
            await _db.SaveChangesAsync();
            return true;
        }

        public async Task<bool> CurateAugmentationAsync(long recipeId, long augmentationId, long personId)
        {
            var entity = await _db.RecipeAugmentations
                .FirstOrDefaultAsync(e => e.Id == augmentationId && e.RecipeId == recipeId && !e.IsDeleted);
            if (entity == null)
            {
                return false;
            }

            Curate(personId, () => entity.CurationStatusId = (long)CurationStatusEnum.Curated, entity);
            await _db.SaveChangesAsync();
            return true;
        }

        private static void SoftDelete(BaseEntity entity, long personId)
        {
            entity.IsDeleted = true;
            entity.DeletedAt = DateTime.UtcNow;
            entity.DeletedByPersonId = personId;
        }

        private static void Curate(long personId, Action setStatus, BaseEntity entity)
        {
            setStatus();
            entity.LastModifiedDate = DateTime.UtcNow;
            entity.LastModifiedByPersonId = personId;
        }

        private static RecipeSubstitutionModel MapSubstitution(RecipeSubstitutionEntity entity) => new()
        {
            Id = entity.Id,
            IngredientId = entity.IngredientId,
            SubstituteIngredientId = entity.SubstituteIngredientId,
            SubstituteName = entity.SubstituteIngredient?.Name ?? string.Empty,
            Ratio = entity.Ratio,
            SubstituteQuantity = entity.SubstituteQuantity,
            SubstituteMeasurementId = entity.SubstituteMeasurementId,
            SubstituteMeasurement = entity.SubstituteMeasurement?.Name,
            Notes = entity.Notes,
            IsCurated = entity.CurationStatusId == (long)CurationStatusEnum.Curated,
            StepEffects = entity.StepEffects
                .Where(e => !e.IsDeleted)
                .OrderBy(e => e.StepNumber)
                .Select(e => new RecipeSubstitutionStepEffectModel
                {
                    Id = e.Id,
                    StepNumber = e.StepNumber,
                    AlteredDescription = e.AlteredDescription,
                    NewTemperatureFahrenheit = e.NewTemperatureFahrenheit,
                    DurationDeltaMinutes = e.DurationDeltaMinutes,
                }).ToList(),
        };

        private static RecipeAugmentationModel MapAugmentation(RecipeAugmentationEntity entity) => new()
        {
            Id = entity.Id,
            IngredientId = entity.IngredientId,
            IngredientName = entity.Ingredient?.Name ?? string.Empty,
            Quantity = entity.Quantity,
            MeasurementId = entity.MeasurementId,
            Measurement = entity.Measurement?.Name,
            FlavorEffect = entity.FlavorEffect,
            Instructions = entity.Instructions,
            InsertAfterStepNumber = entity.InsertAfterStepNumber,
            NewTemperatureFahrenheit = entity.NewTemperatureFahrenheit,
            DurationDeltaMinutes = entity.DurationDeltaMinutes,
            IsCurated = entity.CurationStatusId == (long)CurationStatusEnum.Curated,
        };
    }
}

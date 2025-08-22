using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nom.Data;
using Nom.Data.Plan;
using Nom.Data.Person;
using Nom.Data.Reference;
using Nom.Data.Recipe;
using Nom.Orch.Interfaces;
using Nom.Orch.Models.Plan;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Nom.Orch.Services
{
    public class PlanOrchestrationService : IPlanOrchestrationService
    {
        private readonly ApplicationDbContext _db;
        private readonly ILogger<PlanOrchestrationService> _logger;

        public PlanOrchestrationService(ApplicationDbContext db, ILogger<PlanOrchestrationService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<List<PlanModel>> GetCuratedPlansAsync()
        {
            _logger.LogInformation("Retrieving curated plans");

            var plans = await _db.Plans
                .Include(p => p.Author)
                .Include(p => p.CurationStatus)
                .Include(p => p.Goals)
                .ThenInclude(g => g.GoalItems)
                .Include(p => p.Meals)
                .ThenInclude(m => m.Recipes)
                .Include(p => p.Restrictions)
                .Where(p => p.CurationStatus != null && p.CurationStatus.Name == "Curated")
                .Select(p => new PlanModel
                {
                    Id = p.Id,
                    Name = p.Name,
                    Description = p.Description,
                    StartDate = p.StartDate,
                    EndDate = p.EndDate,
                    InvitationCode = p.InvitationCode,
                    CurationStatus = p.CurationStatus != null ? p.CurationStatus.Name : "NonCurated",
                    AuthorId = p.AuthorId,
                    AuthorName = p.Author != null ? p.Author.Name : string.Empty,
                    DateSubmittedForCuration = p.DateSubmittedForCuration,
                    DateCurationCompleted = p.DateCurationCompleted,
                    ParentPlanId = p.ParentPlanId,
                    Version = p.Version,
                    CreatedDate = p.CreatedDate,
                    LastModifiedDate = p.LastModifiedDate,
                    Goals = p.Goals.Select(g => new GoalModel
                    {
                        Id = g.Id,
                        Name = g.Name,
                        Description = g.Description,
                        GoalType = g.GoalType != null ? g.GoalType.Name : null,
                        BeginDate = g.BeginDate,
                        EndDate = g.EndDate,
                        GoalItems = g.GoalItems != null ? g.GoalItems.Select(gi => new GoalItemModel
                        {
                            Id = gi.Id,
                            Name = gi.Name,
                            Description = gi.Description,
                            IsQuantifiable = gi.IsQuantifiable,
                            IngredientName = gi.Ingredient != null ? gi.Ingredient.Name : null,
                            NutrientName = gi.Nutrient != null ? gi.Nutrient.Name : null,
                            TimeframeType = gi.TimeframeType != null ? gi.TimeframeType.Name : null,
                            Measurement = gi.Measurement != null ? gi.Measurement.Name : null,
                            MeasurementMinimum = gi.MeasurementMinimum,
                            MeasurementMaximum = gi.MeasurementMaximum
                        }).ToList() : new List<GoalItemModel>()
                    }).ToList(),
                    Meals = p.Meals.Select(m => new MealModel
                    {
                        Id = m.Id,
                        MealType = m.MealType != null ? m.MealType.Name : string.Empty,
                        Date = m.Date,
                        Recipes = m.Recipes != null ? m.Recipes.Select(r => new RecipeModel
                        {
                            Id = r.Id,
                            Name = r.Name,
                            Description = r.Description,
                            CurationStatus = r.CurationStatus != null ? r.CurationStatus.Name : "NonCurated"
                        }).ToList() : new List<RecipeModel>()
                    }).ToList(),
                    Restrictions = p.Restrictions.Select(r => new RestrictionModel
                    {
                        Id = r.Id,
                        Name = r.Name,
                        Description = r.Description,
                        RestrictionType = r.RestrictionType != null ? r.RestrictionType.Name : null,
                        IngredientName = r.Ingredient != null ? r.Ingredient.Name : null,
                        NutrientName = r.Nutrient != null ? r.Nutrient.Name : null
                    }).ToList()
                })
                .ToListAsync();

            return plans;
        }

        public async Task<List<PlanModel>> GetMyPlansAsync(long authorId)
        {
            _logger.LogInformation("Retrieving plans for author {AuthorId}", authorId);

            var plans = await _db.Plans
                .Include(p => p.Author)
                .Include(p => p.CurationStatus)
                .Include(p => p.Goals)
                .ThenInclude(g => g.GoalItems)
                .Include(p => p.Meals)
                .ThenInclude(m => m.Recipes)
                .Include(p => p.Restrictions)
                .Where(p => p.AuthorId == authorId)
                .Select(p => new PlanModel
                {
                    Id = p.Id,
                    Name = p.Name,
                    Description = p.Description,
                    StartDate = p.StartDate,
                    EndDate = p.EndDate,
                    InvitationCode = p.InvitationCode,
                    CurationStatus = p.CurationStatus != null ? p.CurationStatus.Name : "NonCurated",
                    AuthorId = p.AuthorId,
                    AuthorName = p.Author != null ? p.Author.Name : string.Empty,
                    DateSubmittedForCuration = p.DateSubmittedForCuration,
                    DateCurationCompleted = p.DateCurationCompleted,
                    ParentPlanId = p.ParentPlanId,
                    Version = p.Version,
                    CreatedDate = p.CreatedDate,
                    LastModifiedDate = p.LastModifiedDate,
                    Goals = p.Goals.Select(g => new GoalModel
                    {
                        Id = g.Id,
                        Name = g.Name,
                        Description = g.Description,
                        GoalType = g.GoalType != null ? g.GoalType.Name : null,
                        BeginDate = g.BeginDate,
                        EndDate = g.EndDate,
                        GoalItems = g.GoalItems != null ? g.GoalItems.Select(gi => new GoalItemModel
                        {
                            Id = gi.Id,
                            Name = gi.Name,
                            Description = gi.Description,
                            IsQuantifiable = gi.IsQuantifiable,
                            IngredientName = gi.Ingredient != null ? gi.Ingredient.Name : null,
                            NutrientName = gi.Nutrient != null ? gi.Nutrient.Name : null,
                            TimeframeType = gi.TimeframeType != null ? gi.TimeframeType.Name : null,
                            Measurement = gi.Measurement != null ? gi.Measurement.Name : null,
                            MeasurementMinimum = gi.MeasurementMinimum,
                            MeasurementMaximum = gi.MeasurementMaximum
                        }).ToList() : new List<GoalItemModel>()
                    }).ToList(),
                    Meals = p.Meals.Select(m => new MealModel
                    {
                        Id = m.Id,
                        MealType = m.MealType != null ? m.MealType.Name : string.Empty,
                        Date = m.Date,
                        Recipes = m.Recipes != null ? m.Recipes.Select(r => new RecipeModel
                        {
                            Id = r.Id,
                            Name = r.Name,
                            Description = r.Description,
                            CurationStatus = r.CurationStatus != null ? r.CurationStatus.Name : "NonCurated"
                        }).ToList() : new List<RecipeModel>()
                    }).ToList(),
                    Restrictions = p.Restrictions.Select(r => new RestrictionModel
                    {
                        Id = r.Id,
                        Name = r.Name,
                        Description = r.Description,
                        RestrictionType = r.RestrictionType != null ? r.RestrictionType.Name : null,
                        IngredientName = r.Ingredient != null ? r.Ingredient.Name : null,
                        NutrientName = r.Nutrient != null ? r.Nutrient.Name : null
                    }).ToList()
                })
                .ToListAsync();

            return plans;
        }

        public async Task<PlanModel> GetPlanByIdAsync(long planId)
        {
            _logger.LogInformation("Retrieving plan {PlanId}", planId);

            var plan = await _db.Plans
                .Include(p => p.Author)
                .Include(p => p.CurationStatus)
                .Include(p => p.Goals)
                .ThenInclude(g => g.GoalItems)
                .Include(p => p.Meals)
                .ThenInclude(m => m.Recipes)
                .Include(p => p.Restrictions)
                .Include(p => p.Participants)
                .ThenInclude(pp => pp.Person)
                .FirstOrDefaultAsync(p => p.Id == planId);

            if (plan == null)
                throw new ArgumentException($"Plan with ID {planId} not found");

            return new PlanModel
            {
                Id = plan.Id,
                Name = plan.Name,
                Description = plan.Description,
                StartDate = plan.StartDate,
                EndDate = plan.EndDate,
                InvitationCode = plan.InvitationCode,
                CurationStatus = plan.CurationStatus?.Name ?? "NonCurated",
                AuthorId = plan.AuthorId,
                AuthorName = plan.Author?.Name ?? string.Empty,
                DateSubmittedForCuration = plan.DateSubmittedForCuration,
                DateCurationCompleted = plan.DateCurationCompleted,
                ParentPlanId = plan.ParentPlanId,
                Version = plan.Version,
                CreatedDate = plan.CreatedDate,
                LastModifiedDate = plan.LastModifiedDate,
                Goals = plan.Goals.Select(g => new GoalModel
                {
                    Id = g.Id,
                    Name = g.Name,
                    Description = g.Description,
                    GoalType = g.GoalType?.Name,
                    BeginDate = g.BeginDate,
                    EndDate = g.EndDate,
                    GoalItems = g.GoalItems?.Select(gi => new GoalItemModel
                    {
                        Id = gi.Id,
                        Name = gi.Name,
                        Description = gi.Description,
                        IsQuantifiable = gi.IsQuantifiable,
                        IngredientName = gi.Ingredient?.Name,
                        NutrientName = gi.Nutrient?.Name,
                        TimeframeType = gi.TimeframeType?.Name,
                        Measurement = gi.Measurement?.Name,
                        MeasurementMinimum = gi.MeasurementMinimum,
                        MeasurementMaximum = gi.MeasurementMaximum
                    }).ToList() ?? new List<GoalItemModel>()
                }).ToList(),
                Meals = plan.Meals.Select(m => new MealModel
                {
                    Id = m.Id,
                    MealType = m.MealType.Name,
                    Date = m.Date,
                    Recipes = m.Recipes?.Select(r => new RecipeModel
                    {
                        Id = r.Id,
                        Name = r.Name,
                        Description = r.Description,
                        CurationStatus = r.CurationStatus?.Name ?? "NonCurated"
                    }).ToList() ?? new List<RecipeModel>()
                }).ToList(),
                Restrictions = plan.Restrictions.Select(r => new RestrictionModel
                {
                    Id = r.Id,
                    Name = r.Name,
                    Description = r.Description,
                    RestrictionType = r.RestrictionType?.Name,
                    IngredientName = r.Ingredient?.Name,
                    NutrientName = r.Nutrient?.Name
                }).ToList(),
                Participants = plan.Participants.Select(pp => new PlanParticipantModel
                {
                    Id = pp.Id,
                    PersonId = pp.PersonId,
                    PersonName = pp.Person?.Name ?? string.Empty,
                    Role = pp.Role.Name,
                    JoinedDate = pp.JoinedDate
                }).ToList()
            };
        }

        public async Task<PlanModel> ClonePlanAsync(long sourcePlanId, long newAuthorId, string newPlanName)
        {
            _logger.LogInformation("Cloning plan {SourcePlanId} for author {NewAuthorId} with name {NewPlanName}", sourcePlanId, newAuthorId, newPlanName);

            var sourcePlan = await _db.Plans
                .Include(p => p.Goals)
                .ThenInclude(g => g.GoalItems)
                .Include(p => p.Meals)
                .ThenInclude(m => m.Recipes)
                .Include(p => p.Restrictions)
                .FirstOrDefaultAsync(p => p.Id == sourcePlanId);

            if (sourcePlan == null)
                throw new ArgumentException($"Source plan with ID {sourcePlanId} not found");

            if (sourcePlan.CurationStatus?.Name != "Curated")
                throw new InvalidOperationException("Only curated plans can be cloned");

            // Create new plan
            var newPlan = new PlanEntity
            {
                Name = newPlanName,
                Description = sourcePlan.Description,
                StartDate = sourcePlan.StartDate,
                EndDate = sourcePlan.EndDate,
                CurationStatusId = 9000L, // NonCurated
                AuthorId = newAuthorId,
                ParentPlanId = sourcePlanId,
                Version = 1,
                CreatedDate = DateTime.UtcNow
            };

            _db.Plans.Add(newPlan);
            await _db.SaveChangesAsync();

            // Clone goals
            foreach (var goal in sourcePlan.Goals)
            {
                var newGoal = new GoalEntity
                {
                    PlanId = newPlan.Id,
                    Name = goal.Name,
                    Description = goal.Description,
                    GoalTypeId = goal.GoalTypeId,
                    BeginDate = goal.BeginDate,
                    EndDate = goal.EndDate,
                    CreatedDate = DateTime.UtcNow
                };

                _db.Goals.Add(newGoal);
                await _db.SaveChangesAsync();

                // Clone goal items
                foreach (var goalItem in goal.GoalItems ?? new List<GoalItemEntity>())
                {
                    var newGoalItem = new GoalItemEntity
                    {
                        GoalId = newGoal.Id,
                        Name = goalItem.Name,
                        Description = goalItem.Description,
                        IsQuantifiable = goalItem.IsQuantifiable,
                        IngredientId = goalItem.IngredientId,
                        NutrientId = goalItem.NutrientId,
                        TimeframeTypeId = goalItem.TimeframeTypeId,
                        MeasurementId = goalItem.MeasurementId,
                        MeasurementMinimum = goalItem.MeasurementMinimum,
                        MeasurementMaximum = goalItem.MeasurementMaximum,
                        CreatedDate = DateTime.UtcNow
                    };

                    _db.GoalItems.Add(newGoalItem);
                }
            }

            // Clone meals
            foreach (var meal in sourcePlan.Meals)
            {
                var newMeal = new MealEntity
                {
                    PlanId = newPlan.Id,
                    MealTypeId = meal.MealTypeId,
                    Date = meal.Date,
                    CreatedDate = DateTime.UtcNow
                };

                _db.Meals.Add(newMeal);
                await _db.SaveChangesAsync();

                // Clone meal recipes (many-to-many relationship)
                if (meal.Recipes != null)
                {
                    foreach (var recipe in meal.Recipes)
                    {
                        // Add recipe to meal (this would need a junction table)
                        // For now, we'll assume the relationship is handled elsewhere
                    }
                }
            }

            // Clone restrictions
            foreach (var restriction in sourcePlan.Restrictions)
            {
                var newRestriction = new RestrictionEntity
                {
                    PlanId = newPlan.Id,
                    Name = restriction.Name,
                    Description = restriction.Description,
                    RestrictionTypeId = restriction.RestrictionTypeId,
                    IngredientId = restriction.IngredientId,
                    NutrientId = restriction.NutrientId,
                    CreatedDate = DateTime.UtcNow
                };

                _db.Restrictions.Add(newRestriction);
            }

            await _db.SaveChangesAsync();

            return await GetPlanByIdAsync(newPlan.Id);
        }

        public async Task<PlanModel> CreatePlanAsync(CreatePlanRequest request, long authorId)
        {
            _logger.LogInformation("Creating new plan for author {AuthorId}", authorId);

            var plan = new PlanEntity
            {
                Name = request.Name,
                Description = request.Description,
                StartDate = request.StartDate,
                EndDate = request.EndDate,
                CurationStatusId = 9000L, // NonCurated
                AuthorId = authorId,
                Version = 1,
                CreatedDate = DateTime.UtcNow
            };

            _db.Plans.Add(plan);
            await _db.SaveChangesAsync();

            // Add goals, meals, and restrictions
            // Implementation would be similar to cloning but with new data

            return await GetPlanByIdAsync(plan.Id);
        }

        public async Task UpdatePlanAsync(long planId, UpdatePlanRequest request, long authorId)
        {
            _logger.LogInformation("Updating plan {PlanId} by author {AuthorId}", planId, authorId);

            var plan = await _db.Plans.FirstOrDefaultAsync(p => p.Id == planId);
            if (plan == null)
                throw new ArgumentException($"Plan with ID {planId} not found");

            if (plan.AuthorId != authorId)
                throw new UnauthorizedAccessException("You can only update your own plans");

            if (plan.CurationStatus?.Name == "Curated")
                throw new InvalidOperationException("Curated plans cannot be modified directly. Create a new version instead.");

            plan.Name = request.Name;
            plan.Description = request.Description;
            plan.StartDate = request.StartDate;
            plan.EndDate = request.EndDate;
            plan.LastModifiedDate = DateTime.UtcNow;

            await _db.SaveChangesAsync();
        }

        public async Task DeletePlanAsync(long planId, long authorId)
        {
            _logger.LogInformation("Deleting plan {PlanId} by author {AuthorId}", planId, authorId);

            var plan = await _db.Plans.FirstOrDefaultAsync(p => p.Id == planId);
            if (plan == null)
                throw new ArgumentException($"Plan with ID {planId} not found");

            if (plan.AuthorId != authorId)
                throw new UnauthorizedAccessException("You can only delete your own plans");

            _db.Plans.Remove(plan);
            await _db.SaveChangesAsync();
        }

        public async Task SubmitPlanForCurationAsync(long planId, long authorId)
        {
            _logger.LogInformation("Submitting plan {PlanId} for curation by author {AuthorId}", planId, authorId);

            var plan = await _db.Plans.FirstOrDefaultAsync(p => p.Id == planId);
            if (plan == null)
                throw new ArgumentException($"Plan with ID {planId} not found");

            if (plan.AuthorId != authorId)
                throw new UnauthorizedAccessException("You can only submit your own plans for curation");

            plan.CurationStatusId = 9001L; // PendingCuration
            plan.DateSubmittedForCuration = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }
    }
} 
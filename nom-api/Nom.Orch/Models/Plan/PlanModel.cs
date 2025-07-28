using System;
using System.Collections.Generic;

namespace Nom.Orch.Models.Plan
{
    public class PlanModel
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public DateOnly StartDate { get; set; }
        public DateOnly? EndDate { get; set; }
        public string? InvitationCode { get; set; }
        public string CurationStatus { get; set; } = "NonCurated";
        public long AuthorId { get; set; }
        public string AuthorName { get; set; } = string.Empty;
        public DateTime? DateSubmittedForCuration { get; set; }
        public DateTime? DateCurationCompleted { get; set; }
        public long? ParentPlanId { get; set; }
        public long Version { get; set; } = 1;
        public DateTime CreatedDate { get; set; }
        public DateTime? LastModifiedDate { get; set; }
        
        // Navigation properties
        public List<GoalModel> Goals { get; set; } = new List<GoalModel>();
        public List<MealModel> Meals { get; set; } = new List<MealModel>();
        public List<RestrictionModel> Restrictions { get; set; } = new List<RestrictionModel>();
        public List<PlanParticipantModel> Participants { get; set; } = new List<PlanParticipantModel>();
    }

    public class GoalModel
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string? GoalType { get; set; }
        public DateOnly? BeginDate { get; set; }
        public DateOnly? EndDate { get; set; }
        public List<GoalItemModel> GoalItems { get; set; } = new List<GoalItemModel>();
    }

    public class GoalItemModel
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool IsQuantifiable { get; set; }
        public string? IngredientName { get; set; }
        public string? NutrientName { get; set; }
        public string? TimeframeType { get; set; }
        public string? MeasurementType { get; set; }
        public decimal? MeasurementMinimum { get; set; }
        public decimal? MeasurementMaximum { get; set; }
    }

    public class MealModel
    {
        public long Id { get; set; }
        public string MealType { get; set; } = string.Empty;
        public DateOnly Date { get; set; }
        public List<RecipeModel> Recipes { get; set; } = new List<RecipeModel>();
    }

    public class RecipeModel
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string CurationStatus { get; set; } = "NonCurated";
    }

    public class RestrictionModel
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? RestrictionType { get; set; }
        public string? IngredientName { get; set; }
        public string? NutrientName { get; set; }
    }

    public class PlanParticipantModel
    {
        public long Id { get; set; }
        public long PersonId { get; set; }
        public string PersonName { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public DateTime JoinedDate { get; set; }
    }

    public class CreatePlanRequest
    {
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public DateOnly StartDate { get; set; }
        public DateOnly? EndDate { get; set; }
        public List<GoalModel> Goals { get; set; } = new List<GoalModel>();
        public List<MealModel> Meals { get; set; } = new List<MealModel>();
        public List<RestrictionModel> Restrictions { get; set; } = new List<RestrictionModel>();
    }

    public class UpdatePlanRequest
    {
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public DateOnly StartDate { get; set; }
        public DateOnly? EndDate { get; set; }
        public List<GoalModel> Goals { get; set; } = new List<GoalModel>();
        public List<MealModel> Meals { get; set; } = new List<MealModel>();
        public List<RestrictionModel> Restrictions { get; set; } = new List<RestrictionModel>();
    }
} 
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
} 
namespace Nom.Orch.Models.MealPlan
{
    public class MealPlanShuffleResponseModel
    {
        public int Created { get; set; }
        public int Deleted { get; set; }
        public MealPlanWeekResponseModel Week { get; set; } = new();
    }
}

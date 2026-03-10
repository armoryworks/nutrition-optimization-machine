export interface MealPlanRuleCreateRequest {
  householdId: number;
  mealTypeId: number | null;
  dayOfWeekId: number | null;
  queryFilter: string;
  maxRecipes: number;
  isActive: boolean;
}

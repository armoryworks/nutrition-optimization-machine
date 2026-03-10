export interface MealPlanRule {
  id: number;
  householdId: number;
  mealTypeId: number | null;
  mealTypeName: string | null;
  dayOfWeekId: number | null;
  dayOfWeekName: string | null;
  queryFilter: string;
  maxRecipes: number;
  isActive: boolean;
}

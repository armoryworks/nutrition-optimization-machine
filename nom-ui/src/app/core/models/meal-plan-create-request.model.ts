export interface MealPlanCreateRequest {
  householdId: number;
  date: string;
  mealTypeId: number;
  title: string | null;
  notes: string | null;
  recipeId: number | null;
}

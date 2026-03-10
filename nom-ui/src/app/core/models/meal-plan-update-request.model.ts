export interface MealPlanUpdateRequest {
  date: string;
  mealTypeId: number;
  title: string | null;
  notes: string | null;
  recipeId: number | null;
}

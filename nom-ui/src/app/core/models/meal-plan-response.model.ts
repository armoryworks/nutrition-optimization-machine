export interface MealPlanResponse {
  id: number;
  householdId: number;
  authorId: number;
  date: string;
  mealTypeId: number;
  mealType: string;
  title: string | null;
  notes: string | null;
  recipeId: number | null;
  recipeName: string | null;
  createdDate: string;
  modifiedDate: string | null;
}

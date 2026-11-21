export interface MealPlanCreateRequest {
  householdId: number;
  date: string;
  mealTypeId: number;
  title: string | null;
  notes: string | null;
  recipeId: number | null;
  /** Schedule a standalone whole food instead of a recipe. Mutually exclusive with recipeId. */
  ingredientId?: number | null;
  quantity?: number | null;
  measurementId?: number | null;
  /** The member confirmed "Add anyway" for a prepared meal held for a household with a severe restriction. */
  confirmHeld?: boolean;
}

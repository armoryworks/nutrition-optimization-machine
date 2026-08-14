export interface MealPlanEntry {
  id: number;
  recipeId: number | null;
  recipeName: string | null;
  recipeImage: string | null;
  /** Set when this slot holds a standalone whole food (apple, protein bar) instead of a recipe. */
  ingredientId?: number | null;
  ingredientName?: string | null;
  quantity?: number | null;
  measurementId?: number | null;
  measurementName?: string | null;
  foodGroupId?: number | null;
  foodGroupName?: string | null;
  title: string | null;
  notes: string | null;
  calories: number | null;
  proteinGrams: number | null;
  carbGrams: number | null;
  fatGrams: number | null;
  completedDate: string | null;
  shoppingCompletedAt: string | null;
}

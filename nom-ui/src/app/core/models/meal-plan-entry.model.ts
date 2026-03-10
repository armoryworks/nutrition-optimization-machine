export interface MealPlanEntry {
  id: number;
  recipeId: number | null;
  recipeName: string | null;
  recipeImage: string | null;
  title: string | null;
  notes: string | null;
  calories: number | null;
  proteinGrams: number | null;
  carbGrams: number | null;
  fatGrams: number | null;
  completedDate: string | null;
}
